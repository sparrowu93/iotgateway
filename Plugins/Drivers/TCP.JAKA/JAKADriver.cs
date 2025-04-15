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

namespace TCP.JAKA
{
    public class JAKADriver : IDriver
    {
        private TcpClient? _commandClient; // Client for sending commands (port 10001)
        private TcpClient? _dataClient;    // Client for receiving data (port 10000)
        private Thread? _receiveThread;
        private ConcurrentDictionary<string, JObject> _responseData;
        private bool _isRunning;
        private string? _ip;
        private int _commandPort = 10001;
        private int _dataPort = 10000;
        private readonly object _lockObj = new object();
        private readonly string _device;
        public ILogger _logger { get; set; }

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
        public int Timeout { get; set; } = 3000;
        
        [ConfigParameter("最小周期(毫秒)")]
        public uint MinPeriod { get; set; } = 3000;

        public JAKADriver(string device, ILogger logger)
        {
            _device = device;
            _logger = logger;
            _responseData = new ConcurrentDictionary<string, JObject>();
            DeviceId = device;

            _logger.LogInformation($"Device:[{_device}],Create()");
        
        }

        public bool CheckConfig(Dictionary<string, string> configDict)
        {
            if (configDict.ContainsKey("IP") && 
                configDict.ContainsKey("CommandPort") && 
                configDict.ContainsKey("DataPort"))
            {
                IP = configDict["IP"];
                _ip = IP; // 保持兼容性
                
                if (int.TryParse(configDict["CommandPort"], out int commandPort))
                {
                    CommandPort = commandPort;
                    _commandPort = commandPort; // 保持兼容性
                }
                
                if (int.TryParse(configDict["DataPort"], out int dataPort))
                {
                    DataPort = dataPort;
                    _dataPort = dataPort; // 保持兼容性
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
                if (string.IsNullOrEmpty(IP))
                {
                    _logger.LogError("IP地址未设置");
                    return false;
                }

                // 使用公共属性IP代替私有字段_ip
                _commandClient = new TcpClient();
                _commandClient.Connect(IP, CommandPort);

                _dataClient = new TcpClient();
                _dataClient.Connect(IP, DataPort);

                // 创建并启动数据接收线程
                _isRunning = true;
                _receiveThread = new Thread(ReceiveDataThread); // 修正方法名
                _receiveThread.Start();

                IsConnected = true;
                _logger.LogInformation($"Device:[{_device}],Connect()");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Device:[{_device}],连接失败: {ex.Message}");
                CloseConnections(); // 修正方法名
                return false;
            }
        }
        
        public bool Close()
        {
            CloseConnections();
            IsConnected = false;
            return true;
        }

        private void ReceiveDataThread()
        {
            NetworkStream? stream = null;
            
            try
            {
                if (_dataClient == null)
                    return;
                
                stream = _dataClient.GetStream();
                byte[] buffer = new byte[4096];
                
                while (_isRunning)
                {
                    if (_dataClient.Available > 0)
                    {
                        int bytesRead = stream.Read(buffer, 0, buffer.Length);
                        if (bytesRead > 0)
                        {
                            string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                            ProcessResponse(response);
                        }
                    }
                    Thread.Sleep(50);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"数据接收线程异常: {ex.Message}");
            }
            finally
            {
                stream?.Close();
            }
        }

        private void ProcessResponse(string response)
        {
            try
            {
                JObject responseObj = JObject.Parse(response);
                string? cmdName = responseObj["cmdName"]?.ToString();
                
                if (!string.IsNullOrEmpty(cmdName))
                {
                    _responseData[cmdName] = responseObj;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"解析响应数据失败: {ex.Message}");
            }
        }

        private JObject SendCommand(string command)
        {
            lock (_lockObj)
            {
                try
                {
                    if (_commandClient == null)
                        throw new InvalidOperationException("命令客户端未初始化");
                        
                    // Extract command name
                    JObject cmdObj = JObject.Parse(command);
                    string? cmdName = cmdObj["cmdName"]?.ToString();
                    
                    if (string.IsNullOrEmpty(cmdName))
                        throw new Exception("无效的命令格式，缺少cmdName");

                    // Clear previous response for this command
                    _responseData.TryRemove(cmdName, out _);

                    // Send command
                    byte[] data = Encoding.UTF8.GetBytes(command);
                    NetworkStream stream = _commandClient.GetStream();
                    stream.Write(data, 0, data.Length);
                    
                    // Wait for response (with timeout)
                    int timeout = Timeout;
                    int elapsed = 0;
                    int sleepTime = 50;
                    
                    while (elapsed < timeout)
                    {
                        if (_responseData.TryGetValue(cmdName, out JObject response))
                        {
                            return response;
                        }
                        
                        Thread.Sleep(sleepTime);
                        elapsed += sleepTime;
                    }
                    
                    throw new TimeoutException($"命令 {cmdName} 等待响应超时");
                }
                catch (Exception ex)
                {
                    throw new Exception($"发送命令失败: {ex.Message}");
                }
            }
        }

        [Method("读取关节位置", description: "读取JAKA机器人的关节位置，可通过address指定具体关节索引(0-5)或属性")]
        public DriverReturnValueModel ReadJointPosition(DriverAddressIoArgModel ioarg)
        {
            var ret = new DriverReturnValueModel { StatusType = VaribaleStatusTypeEnum.Good, Message = string.Empty };
            
            try
            {
                // 发送关节位置请求
                string command = "{\"cmdName\":\"get_joint_pos\"}";
                JObject response = SendCommand(command);
                
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
                string command = "{\"cmdName\":\"get_tcp_pos\"}";
                JObject response = SendCommand(command);
                
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

    // 仍然保留原来的通用Read方法，但只是作为调度其他特定方法的入口
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
            // For now, just implement a read-only driver
            return new DriverReturnValueModel
            {
                StatusType = VaribaleStatusTypeEnum.Bad,
                Message = "JAKA驱动当前仅支持读取操作",
                Value = ""
            };
        }

        public Task<RpcResponse> WriteAsync(string deviceId, string value, DriverAddressIoArgModel ioarg)
        {
            // 简化实现，不支持写入操作
            return Task.FromResult(new RpcResponse
            {
                IsSuccess = false,
                Description = "JAKA驱动当前仅支持读取操作"
            });
        }

        public void Dispose()
        {
            CloseConnections();
        }

        private void CloseConnections()
        {
            _isRunning = false;
            
            if (_receiveThread != null && _receiveThread.IsAlive)
            {
                try { _receiveThread.Join(1000); } catch { }
                _receiveThread = null;
            }
            
            _commandClient?.Close();
            _commandClient = null;
            
            _dataClient?.Close();
            _dataClient = null;
        }
    }
}