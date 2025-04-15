using PluginInterface;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.IO;

namespace TCP.JAKA
{
    public class JAKADriver : IDriver
    {
        // 基本配置
        private string _ip = "127.0.0.1";
        private int _commandPort = 10001;
        private int _dataPort = 10000;
        private readonly string _device;
        public ILogger _logger { get; set; }
        
        // TCP客户端
        private TcpClient? _commandClient;  // 命令端口客户端 (10001)
        private NetworkStream? _commandStream;
        private TcpClient? _dataClient;     // 数据端口客户端 (10000)
        private NetworkStream? _dataStream;
        private Thread? _dataReceiveThread;
        private bool _isRunning;
        private readonly object _lockObj = new object();
        
        // 响应数据缓存
        private ConcurrentDictionary<string, JObject> _responseData;
        private JObject? _lastJointPositionData;
        private JObject? _lastTcpPositionData;
        private DateTime _lastDataReceiveTime;

        [ConfigParameter("设备ID")]
        public string DeviceId { get; set; }
        
        [ConfigParameter("IP地址")]
        public string IP { get; set; } = "127.0.0.1";
        
        [ConfigParameter("命令端口")]
        public int CommandPort { get; set; } = 10001;
        
        [ConfigParameter("数据端口")]
        public int DataPort { get; set; } = 10000;
        
        public bool IsConnected { get; private set; }
        
        [ConfigParameter("超时时间(毫秒)")]
        public int Timeout { get; set; } = 5000;
        
        [ConfigParameter("最小周期(毫秒)")]
        public uint MinPeriod { get; set; } = 3000;
        
        // 调试开关
        [ConfigParameter("调试模式")]
        public bool DebugMode { get; set; } = true;

        public JAKADriver(string device, ILogger logger)
        {
            _device = device;
            _logger = logger;
            DeviceId = device;
            IsConnected = false;
            _responseData = new ConcurrentDictionary<string, JObject>();
            _lastDataReceiveTime = DateTime.MinValue;

            _logger.LogInformation($"Device:[{_device}],Create()");
        }

        public bool CheckConfig(Dictionary<string, string> configDict)
        {
            if (configDict.ContainsKey("IP") && 
                configDict.ContainsKey("CommandPort") && 
                configDict.ContainsKey("DataPort"))
            {
                IP = configDict["IP"];
                _ip = IP;
                
                if (int.TryParse(configDict["CommandPort"], out int commandPort))
                {
                    CommandPort = commandPort;
                    _commandPort = commandPort;
                }
                
                if (int.TryParse(configDict["DataPort"], out int dataPort))
                {
                    DataPort = dataPort;
                    _dataPort = dataPort;
                }
                
                // 检查调试模式
                if (configDict.ContainsKey("DebugMode") && 
                    bool.TryParse(configDict["DebugMode"], out bool debugMode))
                {
                    DebugMode = debugMode;
                }
                
                return true;
            }
            
            return false;
        }

        public bool Init(Dictionary<string, string> configDict)
        {
            if (!CheckConfig(configDict))
                return false;
            
            return true;
        }
        
        public bool Connect()
        {
            try
            {
                // 关闭可能存在的连接
                Close();
                
                _logger.LogInformation($"正在连接JAKA机器人，IP：{IP}，命令端口：{CommandPort}，数据端口：{DataPort}");
                
                // 创建并连接命令客户端
                _commandClient = new TcpClient();
                _commandClient.ReceiveTimeout = Timeout;
                _commandClient.SendTimeout = Timeout;
                _commandClient.NoDelay = true; // 禁用Nagle算法，减少延迟
                _commandClient.Connect(IP, CommandPort);
                _commandStream = _commandClient.GetStream();
                _logger.LogInformation($"命令客户端已连接到 {IP}:{CommandPort}");
                
                // 创建并连接数据客户端
                _dataClient = new TcpClient();
                _dataClient.ReceiveTimeout = Timeout;
                _dataClient.SendTimeout = Timeout;
                _dataClient.NoDelay = true;
                _dataClient.Connect(IP, DataPort);
                _dataStream = _dataClient.GetStream();
                _logger.LogInformation($"数据客户端已连接到 {IP}:{DataPort}");
                
                // 启动数据接收线程
                _isRunning = true;
                _dataReceiveThread = new Thread(DataReceiveThreadFunc);
                _dataReceiveThread.IsBackground = true;
                _dataReceiveThread.Start();
                
                // 等待数据接收线程启动
                Thread.Sleep(200);
                
                // 发送测试命令
                try
                {
                    _logger.LogInformation("发送测试命令 (get_joint_pos)...");
                    
                    JObject? response = SendCommand("get_joint_pos");
                    if (response != null && response["errorCode"]?.ToString() == "0")
                    {
                        _logger.LogInformation("连接测试成功！");
                        _lastJointPositionData = response;
                    }
                    else
                    {
                        _logger.LogWarning($"连接测试异常: {(response == null ? "无响应" : response["errorMsg"])}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"连接测试异常: {ex.Message}");
                    // 连接测试失败不影响返回结果
                }
                
                IsConnected = true;
                _logger.LogInformation($"Device:[{_device}],Connect成功");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Device:[{_device}],连接失败: {ex.Message}");
                Close();
                return false;
            }
        }
        
        public bool Close()
        {
            IsConnected = false;
            
            // 停止数据接收线程
            _isRunning = false;
            
            if (_dataReceiveThread != null && _dataReceiveThread.IsAlive)
            {
                try
                {
                    _dataReceiveThread.Join(1000);
                }
                catch { }
                _dataReceiveThread = null;
            }
            
            // 关闭数据流和客户端
            if (_dataStream != null)
            {
                try { _dataStream.Close(); } catch { }
                _dataStream = null;
            }
            
            if (_dataClient != null)
            {
                try { _dataClient.Close(); } catch { }
                _dataClient = null;
            }
            
            // 关闭命令流和客户端
            if (_commandStream != null)
            {
                try { _commandStream.Close(); } catch { }
                _commandStream = null;
            }
            
            if (_commandClient != null)
            {
                try { _commandClient.Close(); } catch { }
                _commandClient = null;
            }
            
            _logger.LogInformation($"Device:[{_device}],Close()");
            return true;
        }
        
        /// <summary>
        /// 发送命令并等待响应
        /// </summary>
        private JObject? SendCommand(string cmdName)
        {
            lock (_lockObj) // 防止并发发送命令
            {
                if (_commandClient == null || _commandStream == null || !_commandClient.Connected)
                {
                    _logger.LogError("命令通道未初始化或已断开");
                    return null;
                }
                
                try
                {
                    // 构建命令
                    string command = $"{{\"cmdName\":\"{cmdName}\"}}";
                    
                    if (DebugMode)
                    {
                        _logger.LogDebug($"发送命令: {command}");
                    }
                    
                    // 确保命令结尾有换行符
                    if (!command.EndsWith("\n"))
                    {
                        command += "\n";
                    }
                    
                    // 发送命令
                    byte[] sendData = Encoding.UTF8.GetBytes(command);
                    _commandStream.Write(sendData, 0, sendData.Length);
                    _commandStream.Flush();
                    
                    // 等待响应
                    JObject? response = WaitForResponse(cmdName);
                    
                    // 如果没有收到响应，尝试重新发送一次
                    if (response == null)
                    {
                        _logger.LogWarning($"未收到响应，重新发送命令: {cmdName}");
                        _commandStream.Write(sendData, 0, sendData.Length);
                        _commandStream.Flush();
                        
                        // 再次等待响应
                        response = WaitForResponse(cmdName);
                    }
                    
                    return response;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"发送命令异常: {ex.Message}");
                    return null;
                }
            }
        }
        
        /// <summary>
        /// 等待指定命令的响应
        /// </summary>
        private JObject? WaitForResponse(string cmdName)
        {
            // 记录等待开始时间
            DateTime startTime = DateTime.Now;
            
            // 等待响应数据
            while ((DateTime.Now - startTime).TotalMilliseconds < Timeout)
            {
                // 检查响应字典中是否有对应命令的响应
                if (_responseData.TryRemove(cmdName, out JObject response))
                {
                    if (DebugMode)
                    {
                        _logger.LogDebug($"收到命令 {cmdName} 的响应: {response}");
                    }
                    return response;
                }
                
                // 短暂等待，避免CPU占用
                Thread.Sleep(10);
            }
            
            _logger.LogWarning($"等待命令 {cmdName} 响应超时");
            return null;
        }
        
        /// <summary>
        /// 数据接收线程函数
        /// </summary>
        private void DataReceiveThreadFunc()
        {
            try
            {
                _logger.LogInformation("数据接收线程已启动");
                
                if (_dataClient == null || _dataStream == null)
                {
                    _logger.LogError("数据通道未初始化");
                    return;
                }
                
                byte[] buffer = new byte[4096];
                StringBuilder messageBuilder = new StringBuilder();
                
                while (_isRunning)
                {
                    try
                    {
                        if (_dataClient.Available > 0)
                        {
                            int bytesRead = _dataStream.Read(buffer, 0, buffer.Length);
                            if (bytesRead > 0)
                            {
                                string data = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                                
                                if (DebugMode)
                                {
                                    _logger.LogDebug($"接收到数据 ({bytesRead} 字节): {data}");
                                }
                                
                                // 添加数据到缓冲区
                                messageBuilder.Append(data);
                                
                                // 处理可能的多个或部分JSON消息
                                ProcessJsonMessages(messageBuilder);
                                
                                // 更新最后接收数据的时间
                                _lastDataReceiveTime = DateTime.Now;
                            }
                        }
                        
                        // 降低CPU使用率
                        Thread.Sleep(10);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"数据接收异常: {ex.Message}");
                        Thread.Sleep(100); // 出错后短暂暂停
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"数据接收线程异常: {ex.Message}");
            }
            finally
            {
                _logger.LogInformation("数据接收线程已退出");
            }
        }
        
        /// <summary>
        /// 处理接收到的JSON消息
        /// </summary>
        private void ProcessJsonMessages(StringBuilder messageBuilder)
        {
            string message = messageBuilder.ToString();
            int startIndex = 0;
            int processedIndex = 0;
            
            while (startIndex < message.Length)
            {
                // 查找JSON开始位置
                int jsonStart = message.IndexOf('{', startIndex);
                if (jsonStart == -1) break;
                
                // 查找匹配的JSON结束位置
                int jsonEnd = FindMatchingBrace(message, jsonStart);
                if (jsonEnd == -1) break;
                
                // 提取完整的JSON字符串
                string jsonText = message.Substring(jsonStart, jsonEnd - jsonStart + 1);
                
                try
                {
                    // 解析JSON
                    JObject jsonObj = JObject.Parse(jsonText);
                    string? cmdName = jsonObj["cmdName"]?.ToString();
                    
                    if (!string.IsNullOrEmpty(cmdName))
                    {
                        // 存储响应
                        _responseData[cmdName] = jsonObj;
                        
                        // 根据命令类型缓存特定数据
                        if (cmdName == "get_joint_pos" && jsonObj["errorCode"]?.ToString() == "0")
                        {
                            _lastJointPositionData = jsonObj;
                        }
                        else if (cmdName == "get_tcp_pos" && jsonObj["errorCode"]?.ToString() == "0")
                        {
                            _lastTcpPositionData = jsonObj;
                        }
                        
                        if (DebugMode)
                        {
                            _logger.LogDebug($"处理命令 {cmdName} 的响应");
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (DebugMode)
                    {
                        _logger.LogWarning($"JSON解析异常: {ex.Message}, 内容: {jsonText}");
                    }
                }
                
                // 更新处理位置
                processedIndex = jsonEnd + 1;
                startIndex = processedIndex;
            }
            
            // 保留未处理的部分
            if (processedIndex > 0)
            {
                messageBuilder.Remove(0, processedIndex);
            }
        }
        
        /// <summary>
        /// 查找与指定位置的开括号匹配的闭括号
        /// </summary>
        private int FindMatchingBrace(string text, int openBraceIndex)
        {
            int count = 1; // 已找到一个开括号
            
            for (int i = openBraceIndex + 1; i < text.Length; i++)
            {
                if (text[i] == '{')
                {
                    count++;
                }
                else if (text[i] == '}')
                {
                    count--;
                    if (count == 0)
                    {
                        return i; // 找到匹配的闭括号
                    }
                }
            }
            
            return -1; // 未找到匹配的闭括号
        }

        [Method("读取关节位置", description: "读取JAKA机器人的关节位置，可通过address指定具体关节索引(0-5)或属性")]
        public DriverReturnValueModel ReadJointPosition(DriverAddressIoArgModel ioarg)
        {
            var ret = new DriverReturnValueModel { StatusType = VaribaleStatusTypeEnum.Good, Message = string.Empty };
            
            try
            {
                // 发送关节位置请求
                JObject? response = SendCommand("get_joint_pos");
                
                // 如果请求失败但有缓存数据，使用缓存
                if (response == null)
                {
                    _logger.LogWarning("无法获取实时关节位置，尝试使用缓存");
                    response = _lastJointPositionData;
                    
                    if (response == null)
                    {
                        ret.StatusType = VaribaleStatusTypeEnum.Bad;
                        ret.Message = "获取关节位置失败: 无响应且无缓存数据";
                        return ret;
                    }
                    
                    ret.Message = "使用缓存的关节位置数据";
                }
                else
                {
                    // 更新缓存
                    _lastJointPositionData = response;
                }
                
                if (response["errorCode"]?.ToString() == "0")
                {
                    JArray? jointPosArray = response["joint_pos"] as JArray;
                    
                    if (jointPosArray == null)
                    {
                        ret.StatusType = VaribaleStatusTypeEnum.Bad;
                        ret.Message = "获取关节位置失败: 响应数据格式不正确";
                        return ret;
                    }
                    
                    // 如果没有指定address或address为空，返回完整的关节位置数组
                    if (string.IsNullOrEmpty(ioarg.Address))
                    {
                        ret.Value = jointPosArray.ToString();
                        return ret;
                    }
                    
                    // 尝试解析为数字索引（关节索引0-5）
                    if (int.TryParse(ioarg.Address, out int jointIndex) && jointIndex >= 0 && jointIndex < jointPosArray.Count)
                    {
                        ret.Value = jointPosArray[jointIndex].ToString();
                        return ret;
                    }
                    
                    // 如果不是关节索引，尝试使用JSON路径提取数据
                    try
                    {
                        JToken? token = jointPosArray.SelectToken(ioarg.Address);
                        ret.Value = token?.ToString() ?? string.Empty;
                        return ret;
                    }
                    catch (Exception)
                    {
                        ret.StatusType = VaribaleStatusTypeEnum.Bad;
                        ret.Message = $"无效的关节位置地址: {ioarg.Address}";
                        return ret;
                    }
                }
                else
                {
                    ret.StatusType = VaribaleStatusTypeEnum.Bad;
                    ret.Message = $"获取关节位置失败: {response["errorMsg"]}";
                    return ret;
                }
            }
            catch (Exception ex)
            {
                ret.StatusType = VaribaleStatusTypeEnum.Bad;
                ret.Message = $"读取关节位置异常: {ex.Message}";
                _logger.LogError($"Device:[{DeviceId}],读取关节位置错误: {ex.Message}");
                return ret;
            }
        }

        [Method("读取末端姿态", description: "读取JAKA机器人的末端位姿，可通过address指定具体分量(x,y,z,a,b,c)或属性")]
        public DriverReturnValueModel ReadTcpPosition(DriverAddressIoArgModel ioarg)
        {
            var ret = new DriverReturnValueModel { StatusType = VaribaleStatusTypeEnum.Good, Message = string.Empty };
            
            try
            {
                // 发送TCP位置请求
                JObject? response = SendCommand("get_tcp_pos");
                
                // 如果请求失败但有缓存数据，使用缓存
                if (response == null)
                {
                    _logger.LogWarning("无法获取实时TCP位置，尝试使用缓存");
                    response = _lastTcpPositionData;
                    
                    if (response == null)
                    {
                        ret.StatusType = VaribaleStatusTypeEnum.Bad;
                        ret.Message = "获取TCP位置失败: 无响应且无缓存数据";
                        return ret;
                    }
                    
                    ret.Message = "使用缓存的TCP位置数据";
                }
                else
                {
                    // 更新缓存
                    _lastTcpPositionData = response;
                }
                
                if (response["errorCode"]?.ToString() == "0")
                {
                    JArray? tcpPosArray = response["tcp_pos"] as JArray;
                    
                    if (tcpPosArray == null)
                    {
                        ret.StatusType = VaribaleStatusTypeEnum.Bad;
                        ret.Message = "获取TCP位置失败: 响应数据格式不正确";
                        return ret;
                    }
                    
                    // 如果没有指定address或address为空，返回完整的TCP位置数组
                    if (string.IsNullOrEmpty(ioarg.Address))
                    {
                        ret.Value = tcpPosArray.ToString();
                        return ret;
                    }
                    
                    // 尝试解析为数字索引（TCP位置分量索引，0=x, 1=y, 2=z, 3=a, 4=b, 5=c）
                    if (int.TryParse(ioarg.Address, out int tcpIndex) && tcpIndex >= 0 && tcpIndex < tcpPosArray.Count)
                    {
                        ret.Value = tcpPosArray[tcpIndex].ToString();
                        return ret;
                    }
                    
                    // 支持按名称索引 (x, y, z, a, b, c)
                    if (ioarg.Address.Length == 1)
                    {
                        int index = -1;
                        switch (ioarg.Address.ToLower())
                        {
                            case "x": index = 0; break;
                            case "y": index = 1; break;
                            case "z": index = 2; break;
                            case "a": index = 3; break;
                            case "b": index = 4; break;
                            case "c": index = 5; break;
                        }
                        
                        if (index >= 0 && index < tcpPosArray.Count)
                        {
                            ret.Value = tcpPosArray[index].ToString();
                            return ret;
                        }
                    }
                    
                    // 如果不是索引或简单名称，尝试使用JSON路径提取数据
                    try
                    {
                        JToken? token = tcpPosArray.SelectToken(ioarg.Address);
                        ret.Value = token?.ToString() ?? string.Empty;
                        return ret;
                    }
                    catch (Exception)
                    {
                        ret.StatusType = VaribaleStatusTypeEnum.Bad;
                        ret.Message = $"无效的TCP位置地址: {ioarg.Address}";
                        return ret;
                    }
                }
                else
                {
                    ret.StatusType = VaribaleStatusTypeEnum.Bad;
                    ret.Message = $"获取TCP位置失败: {response["errorMsg"]}";
                    return ret;
                }
            }
            catch (Exception ex)
            {
                ret.StatusType = VaribaleStatusTypeEnum.Bad;
                ret.Message = $"读取TCP位置异常: {ex.Message}";
                _logger.LogError($"Device:[{DeviceId}],读取TCP位置错误: {ex.Message}");
                return ret;
            }
        }

        // 通用读取方法
        [Method("读取变量值", description: "读取JAKA机器人的值，支持关节位置(joint_pos)或TCP位置(tcp_pos)")]
        public DriverReturnValueModel Read(DriverAddressIoArgModel ioarg)
        {
            // 检查address前缀来决定调用哪个方法
            if (string.IsNullOrEmpty(ioarg.Address))
            {
                return new DriverReturnValueModel 
                { 
                    StatusType = VaribaleStatusTypeEnum.Bad, 
                    Message = "地址不能为空，请指定joint_pos或tcp_pos" 
                };
            }
            
            if (ioarg.Address.StartsWith("joint_pos", StringComparison.OrdinalIgnoreCase))
            {
                // 提取子地址（去掉前缀后的部分）
                string subAddress = ioarg.Address.Length > "joint_pos".Length 
                    ? ioarg.Address.Substring("joint_pos".Length).TrimStart('.', '[', ']')
                    : "";
                    
                return ReadJointPosition(new DriverAddressIoArgModel { Address = subAddress });
            }
            else if (ioarg.Address.StartsWith("tcp_pos", StringComparison.OrdinalIgnoreCase))
            {
                // 提取子地址（去掉前缀后的部分）
                string subAddress = ioarg.Address.Length > "tcp_pos".Length 
                    ? ioarg.Address.Substring("tcp_pos".Length).TrimStart('.', '[', ']')
                    : "";
                    
                return ReadTcpPosition(new DriverAddressIoArgModel { Address = subAddress });
            }
            else
            {
                return new DriverReturnValueModel
                {
                    StatusType = VaribaleStatusTypeEnum.Bad,
                    Message = $"不支持的地址: {ioarg.Address}，请使用joint_pos或tcp_pos开头的地址"
                };
            }
        }

        public Task<DriverReturnValueModel> ReadAsync(DriverAddressIoArgModel ioarg)
        {
            return Task.FromResult(Read(ioarg));
        }

        public DriverReturnValueModel Write(string deviceId, string value, DriverAddressIoArgModel ioarg)
        {
            // 暂不支持写入操作
            return new DriverReturnValueModel
            {
                StatusType = VaribaleStatusTypeEnum.Bad,
                Message = "JAKA驱动当前仅支持读取操作",
                Value = ""
            };
        }

        public Task<RpcResponse> WriteAsync(string deviceId, string value, DriverAddressIoArgModel ioarg)
        {
            // 暂不支持写入操作
            return Task.FromResult(new RpcResponse
            {
                IsSuccess = false,
                Description = "JAKA驱动当前仅支持读取操作"
            });
        }

        public void Dispose()
        {
            Close();
        }
        
        [Method("手动发送命令", description: "手动发送命令给JAKA机器人并获取响应")]
        public DriverReturnValueModel ManualCommand(DriverAddressIoArgModel ioarg)
        {
            var ret = new DriverReturnValueModel { StatusType = VaribaleStatusTypeEnum.Good, Message = string.Empty };
            
            try
            {
                string cmdName = ioarg.Address;
                
                if (string.IsNullOrEmpty(cmdName))
                {
                    ret.StatusType = VaribaleStatusTypeEnum.Bad;
                    ret.Message = "命令名称不能为空";
                    return ret;
                }
                
                JObject? response = SendCommand(cmdName);
                
                if (response == null)
                {
                    ret.StatusType = VaribaleStatusTypeEnum.Bad;
                    ret.Message = $"发送命令 {cmdName} 失败: 无响应或响应超时";
                    return ret;
                }
                
                ret.Value = response.ToString();
                return ret;
            }
            catch (Exception ex)
            {
                ret.StatusType = VaribaleStatusTypeEnum.Bad;
                ret.Message = $"发送命令异常: {ex.Message}";
                return ret;
            }
        }
    }
}