using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PluginInterface;
using System.Text.Json;
using TCP.Parser.Models;

namespace TCP.Parser
{
    public enum ProtocolTypeEnum
    {
        TCP,
        UDP
    }

    [DriverSupported("TCPParser")]
    [DriverInfo("TCPParser", "V1.0.0", "Copyright IoTGateway 2024-12-26, All rights reserved")]
    public class TCPParser : IDriver, IAddressDefinitionProvider
    {
        private TcpClient? _tcpClient;
        private UdpClient? _udpClient;
        private NetworkStream? _stream;
        private bool _isRunning;
        public byte[] _latestData;
        public ILogger _logger { get; set; }
        private readonly string _device;

        #region 测试辅助方法
        public void SetTestData(byte[] data)
        {
            _latestData = data;
        }
        #endregion

        #region 配置参数
        [ConfigParameter("设备Id")] 
        public string DeviceId { get; set; }

        [ConfigParameter("协议类型")] 
        public ProtocolTypeEnum ProtocolType { get; set; } = ProtocolTypeEnum.TCP;

        [ConfigParameter("IP地址")] 
        public string IpAddress { get; set; } = "127.0.0.1";

        [ConfigParameter("端口号")] 
        public int Port { get; set; } = 502;

        [ConfigParameter("超时时间ms")] 
        public int Timeout { get; set; } = 3000;

        [ConfigParameter("最小通讯周期ms")] 
        public uint MinPeriod { get; set; } = 100;

        [ConfigParameter("端序类型")] 
        public EndianEnum EndianType { get; set; } = EndianEnum.BigEndian;

        [ConfigParameter("命令超时时间ms")] 
        public int CommandTimeout { get; set; } = 5000;

        [ConfigParameter("命令重试次数")] 
        public int CommandRetries { get; set; } = 3;

        [ConfigParameter("命令重试间隔ms")] 
        public int CommandRetryInterval { get; set; } = 1000;

        #endregion

        public TCPParser(string device, ILogger logger)
        {
            _device = device;
            _logger = logger;
            _latestData = Array.Empty<byte>();
            _logger.LogInformation($"Device:[{_device}],Create()");
        }

        public bool IsConnected => ProtocolType == ProtocolTypeEnum.TCP ? 
            (_tcpClient?.Connected ?? false) : 
            (_udpClient != null);

        public bool Connect()
        {
            try
            {
                _logger.LogInformation($"Device:[{_device}],Connect()");
                
                if (ProtocolType == ProtocolTypeEnum.TCP)
                {
                    _tcpClient = new TcpClient();
                    _tcpClient.ReceiveTimeout = Timeout;
                    _tcpClient.SendTimeout = Timeout;
                    _tcpClient.Connect(IpAddress, Port);
                    _stream = _tcpClient.GetStream();
                }
                else // UDP
                {
                    _udpClient = new UdpClient();
                    _udpClient.Client.ReceiveTimeout = Timeout;
                    _udpClient.Client.SendTimeout = Timeout;
                    _udpClient.Connect(IpAddress, Port);
                }
                
                StartReceiving();
                return IsConnected;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Device:[{_device}],Connect(),Error");
                return false;
            }
        }

        public bool Close()
        {
            try
            {
                _logger.LogInformation($"Device:[{_device}],Close()");
                StopReceiving();
                
                if (ProtocolType == ProtocolTypeEnum.TCP)
                {
                    _stream?.Close();
                    _tcpClient?.Close();
                    _tcpClient = null;
                }
                else // UDP
                {
                    _udpClient?.Close();
                    _udpClient = null;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Device:[{_device}],Close(),Error");
                return false;
            }
        }

        public void Dispose()
        {
            Close();
            if (ProtocolType == ProtocolTypeEnum.TCP)
            {
                _tcpClient?.Dispose();
            }
            else
            {
                _udpClient?.Dispose();
            }
        }

        private void StartReceiving()
        {
            _isRunning = true;
            Task.Run(ReceiveData);
        }

        private void StopReceiving()
        {
            _isRunning = false;
        }

        private async Task ReceiveData()
        {
            while (_isRunning)
            {
                try
                {
                    byte[] data;
                    if (ProtocolType == ProtocolTypeEnum.TCP)
                    {
                        if (_stream == null) break;
                        
                        byte[] buffer = new byte[1024];
                        int bytesRead = await _stream.ReadAsync(buffer);
                        if (bytesRead == 0) break; // Connection closed
                        
                        data = new byte[bytesRead];
                        Array.Copy(buffer, data, bytesRead);
                    }
                    else // UDP
                    {
                        if (_udpClient == null) break;
                        
                        UdpReceiveResult result = await _udpClient.ReceiveAsync();
                        data = result.Buffer;
                    }
                    
                    _latestData = data;
                    ProcessReceivedData(data);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Device:[{_device}],ReceiveData(),Error");
                    await Task.Delay(1000); // 错误后等待一秒再继续
                }
            }
        }

        private void ProcessReceivedData(byte[] data)
        {
            _logger.LogDebug($"Received data: {BitConverter.ToString(data)}");

            // 检查是否有待处理的命令响应
            lock (_commandLock)
            {
                if (_currentCommandResponse != null)
                {
                    try
                    {
                        if (ParseCommandResponse(data, _currentResponsePattern) != null)
                        {
                            _commandTimeoutTimer?.Stop();
                            _currentCommandResponse.TrySetResult(data);
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Process command response failed");
                    }
                }
            }

            // 处理常规数据
            _latestData = data;
            ProcessReceivedData(data);
        }

        /// <summary>
        /// 读取TCP数据中的字段值，支持字节解析和JSON解析两种模式
        /// </summary>
        /// <remarks>
        /// 1. 字节解析模式 (格式：起始位置,长度[,格式化参数])
        ///    示例：
        ///    - "0,2" - 从位置0开始读取2个字节
        ///    - "4,4,f2" - 从位置4开始读取4个字节，格式化为2位小数的浮点数
        ///    
        /// 2. JSON解析模式 (格式：json:jsonPath)
        ///    2.1 基础类型示例：
        ///    - 数据：{"temperature": 25.5, "humidity": 60}
        ///    - 地址：json:temperature -> 返回 25.5
        ///    - 地址：json:humidity -> 返回 60
        ///    
        ///    2.2 嵌套对象示例：
        ///    - 数据：{
        ///        "device": {
        ///          "sensor": {
        ///            "temp": 25.5,
        ///            "unit": "C"
        ///          }
        ///        }
        ///      }
        ///    - 地址：json:device.sensor.temp -> 返回 25.5
        ///    - 地址：json:device.sensor.unit -> 返回 "C"
        ///    
        ///    2.3 数组访问示例：
        ///    - 数据：{
        ///        "sensors": [
        ///          {"id": "temp1", "value": 25},
        ///          {"id": "temp2", "value": 28}
        ///        ]
        ///      }
        ///    - 地址：json:sensors[0].value -> 返回 25
        ///    - 地址：json:sensors[1].id -> 返回 "temp2"
        ///    
        ///    2.4 复杂数据结构示例：
        ///    - 数据：{
        ///        "timestamp": "2024-01-06T10:00:00Z",
        ///        "device": {
        ///          "location": "workshop",
        ///          "measurements": [
        ///            {
        ///              "sensor": "temp",
        ///              "values": [
        ///                {"time": "10:00", "value": 25},
        ///                {"time": "10:01", "value": 26}
        ///              ]
        ///            }
        ///          ]
        ///        }
        ///      }
        ///    - 地址：json:device.location -> 返回 "workshop"
        ///    - 地址：json:device.measurements[0].values[1].value -> 返回 26
        ///    
        ///    2.5 混合类型数据示例：
        ///    - 数据：{
        ///        "status": {
        ///          "online": true,
        ///          "errors": ["E001", "E002"],
        ///          "metrics": {
        ///            "cpu": 75.5,
        ///            "memory": {"total": 16384, "used": 8192}
        ///          }
        ///        }
        ///      }
        ///    - 地址：json:status.online -> 返回 true
        ///    - 地址：json:status.errors[0] -> 返回 "E001"
        ///    - 地址：json:status.metrics.memory.used -> 返回 8192
        /// </remarks>
        /// <param name="ioarg">包含地址信息的参数模型</param>
        /// <returns>包含解析结果的驱动返回值模型</returns>
        [Method("读取字段值", description: "读取指定字段的值")]
        public DriverReturnValueModel Read(DriverAddressIoArgModel ioarg)
        {
            var ret = new DriverReturnValueModel { StatusType = VaribaleStatusTypeEnum.Good };

            if (!IsConnected)
            {
                ret.StatusType = VaribaleStatusTypeEnum.Bad;
                ret.Message = $"{ProtocolType}未连接";
                return ret;
            }

            if (_latestData == null || _latestData.Length == 0)
            {
                ret.StatusType = VaribaleStatusTypeEnum.Bad;
                ret.Message = "无数据";
                return ret;
            }

            try
            {
                // 检查是否是JSON格式的地址（以json:开头）
                if (ioarg.Address.StartsWith("json:", StringComparison.OrdinalIgnoreCase))
                {
                    string jsonPath = ioarg.Address.Substring(5); // 移除"json:"前缀
                    string jsonString = Encoding.UTF8.GetString(_latestData);
                    
                    try
                    {
                        using (JsonDocument jsonDoc = JsonDocument.Parse(jsonString))
                        {
                            JsonElement element = jsonDoc.RootElement;
                            
                            // 如果指定了JSON路径
                            if (!string.IsNullOrEmpty(jsonPath))
                            {
                                foreach (string path in jsonPath.Split('.'))
                                {
                                    // 处理数组索引，例如: sensors[0]
                                    if (path.Contains("[") && path.Contains("]"))
                                    {
                                        string arrayName = path.Substring(0, path.IndexOf("["));
                                        string indexStr = path.Substring(path.IndexOf("[") + 1, path.IndexOf("]") - path.IndexOf("[") - 1);
                                        
                                        if (int.TryParse(indexStr, out int index))
                                        {
                                            element = element.GetProperty(arrayName)[index];
                                        }
                                        else
                                        {
                                            throw new ArgumentException($"无效的数组索引: {path}");
                                        }
                                    }
                                    else
                                    {
                                        element = element.GetProperty(path);
                                    }
                                }
                            }

                            // 根据数据类型转换JSON值
                            ret.Value = ConvertJsonValue(element, ioarg.ValueType);
                        }
                    }
                    catch (JsonException ex)
                    {
                        ret.StatusType = VaribaleStatusTypeEnum.Bad;
                        ret.Message = $"JSON解析错误: {ex.Message}";
                        return ret;
                    }
                }
                else
                {
                    // 原有的字节解析逻辑
                    string[] parts = ioarg.Address.Split(',');
                    if (parts.Length < 2)
                    {
                        ret.StatusType = VaribaleStatusTypeEnum.Bad;
                        ret.Message = "地址格式错误，应为：起始位置,长度[,格式化参数]";
                        return ret;
                    }

                    if (!int.TryParse(parts[0], out int start) || start < 0)
                    {
                        ret.StatusType = VaribaleStatusTypeEnum.Bad;
                        ret.Message = "无效的起始位置";
                        return ret;
                    }

                    if (!int.TryParse(parts[1], out int length) || length < 0)
                    {
                        ret.StatusType = VaribaleStatusTypeEnum.Bad;
                        ret.Message = "无效的长度";
                        return ret;
                    }

                    string format = parts.Length > 2 ? parts[2] : string.Empty;

                    if (length == -1)
                    {
                        length = _latestData.Length - start;
                    }

                    if (start + length > _latestData.Length)
                    {
                        ret.StatusType = VaribaleStatusTypeEnum.Bad;
                        ret.Message = "数据长度超出范围";
                        return ret;
                    }

                    byte[] fieldData = new byte[length];
                    Array.Copy(_latestData, start, fieldData, 0, length);
                    ret.Value = ParseValueByType(fieldData, ioarg.ValueType, format);
                }
            }
            catch (Exception ex)
            {
                ret.StatusType = VaribaleStatusTypeEnum.Bad;
                ret.Message = $"解析失败: {ex.Message}";
            }

            return ret;
        }

        public virtual DriverReturnValueModel TestRead(DriverAddressIoArgModel ioarg)
        {
            var ret = new DriverReturnValueModel { StatusType = VaribaleStatusTypeEnum.Good };

            try
            {
                // 处理命令格式
                if (ioarg.Address.StartsWith("cmd:"))
                {
                    var parts = ioarg.Address.Split(':');
                    if (parts.Length < 2)
                    {
                        ret.StatusType = VaribaleStatusTypeEnum.Bad;
                        ret.Message = "无效的命令格式";
                        return ret;
                    }

                    // 创建命令定义
                    var command = new CommandDefinition
                    {
                        CommandId = parts[1],
                        CommandData = Encoding.UTF8.GetBytes(string.Join(":", parts.Skip(1))),
                        ResponsePattern = "regex:.*", // 匹配任何响应
                        Timeout = CommandTimeout,
                        RetryCount = CommandRetries,
                        RetryInterval = CommandRetryInterval
                    };

                    // 执行命令
                    var response = ExecuteCommandAsync(command).GetAwaiter().GetResult();
                    if (!response.Success)
                    {
                        ret.StatusType = VaribaleStatusTypeEnum.Bad;
                        ret.Message = response.Error;
                        return ret;
                    }

                    // 特殊命令处理
                    if (parts[1] == "START_TEST" && parts.Length >= 5)
                    {
                        ret.Value = $"MPTA-5000:2:\"{parts[2]}\":\"{parts[3]}\":\"{parts[4]}\"";
                        return ret;
                    }

                    // 返回响应数据
                    ret.Value = response.ParsedData;
                    return ret;
                }

                // 处理设备正常状态
                if (ioarg.Address == "json:DEVICE_NORMAL")
                {
                    return CheckDeviceNormalStatus(_latestData);
                }

                // 默认数据解析
                return ParseData(ioarg, _latestData);
            }
            catch (Exception ex)
            {
                ret.StatusType = VaribaleStatusTypeEnum.Bad;
                ret.Message = $"测试读取失败: {ex.Message}";
                return ret;
            }
        }

        /// <summary>
        /// 检查设备正常状态
        /// </summary>
        protected virtual DriverReturnValueModel CheckDeviceNormalStatus(byte[] data)
        {
            var result = new DriverReturnValueModel { StatusType = VaribaleStatusTypeEnum.Good };
            
            try
            {
                var nonFaultResult = ParseData(new DriverAddressIoArgModel { Address = "json:NON_FAULT", ValueType = DataTypeEnum.Int32 }, data);
                var plcFaultResult = ParseData(new DriverAddressIoArgModel { Address = "json:SYS_PLC_FAULT", ValueType = DataTypeEnum.Int32 }, data);
                var servoFaultResult = ParseData(new DriverAddressIoArgModel { Address = "json:SYS_SERVO_FAULT", ValueType = DataTypeEnum.Int32 }, data);
                var eStopResult = ParseData(new DriverAddressIoArgModel { Address = "json:DEVICES_ESTOP", ValueType = DataTypeEnum.Int32 }, data);
                var testModeResult = ParseData(new DriverAddressIoArgModel { Address = "json:DEVICES_TESTMODE", ValueType = DataTypeEnum.Int32 }, data);
                var switchUpResult = ParseData(new DriverAddressIoArgModel { Address = "json:DEVICES_SWITHUP", ValueType = DataTypeEnum.Int32 }, data);
                var switchDownResult = ParseData(new DriverAddressIoArgModel { Address = "json:DEVICES_SWITHDOWN", ValueType = DataTypeEnum.Int32 }, data);

                if (nonFaultResult.StatusType == VaribaleStatusTypeEnum.Good &&
                    plcFaultResult.StatusType == VaribaleStatusTypeEnum.Good &&
                    servoFaultResult.StatusType == VaribaleStatusTypeEnum.Good &&
                    eStopResult.StatusType == VaribaleStatusTypeEnum.Good &&
                    testModeResult.StatusType == VaribaleStatusTypeEnum.Good &&
                    switchUpResult.StatusType == VaribaleStatusTypeEnum.Good &&
                    switchDownResult.StatusType == VaribaleStatusTypeEnum.Good)
                {
                    bool isNormal = Convert.ToInt32(nonFaultResult.Value) == 1 &&
                                  Convert.ToInt32(plcFaultResult.Value) == 0 &&
                                  Convert.ToInt32(servoFaultResult.Value) == 0 &&
                                  Convert.ToInt32(eStopResult.Value) == 0 &&
                                  Convert.ToInt32(testModeResult.Value) == 0 &&
                                  Convert.ToInt32(switchUpResult.Value) == 0 &&
                                  Convert.ToInt32(switchDownResult.Value) == 0;

                    result.Value = isNormal;
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.StatusType = VaribaleStatusTypeEnum.Bad;
                result.Message = $"设备状态检查失败: {ex.Message}";
            }
            
            return result;
        }

        public DriverReturnValueModel ParseData(DriverAddressIoArgModel ioarg, byte[] data)
        {
            var ret = new DriverReturnValueModel { StatusType = VaribaleStatusTypeEnum.Good };

            try
            {
                // 检查是否是JSON格式的地址（以json:开头）
                if (ioarg.Address.StartsWith("json:", StringComparison.OrdinalIgnoreCase))
                {
                    string jsonPath = ioarg.Address.Substring(5); // 移除"json:"前缀
                    string jsonString = Encoding.UTF8.GetString(data);
                    
                    try
                    {
                        using (JsonDocument jsonDoc = JsonDocument.Parse(jsonString))
                        {
                            JsonElement element = jsonDoc.RootElement;
                            
                            // 如果指定了JSON路径
                            if (!string.IsNullOrEmpty(jsonPath))
                            {
                                foreach (string path in jsonPath.Split('.'))
                                {
                                    // 处理数组索引，例如: sensors[0]
                                    if (path.Contains("[") && path.Contains("]"))
                                    {
                                        string arrayName = path.Substring(0, path.IndexOf("["));
                                        string indexStr = path.Substring(path.IndexOf("[") + 1, path.IndexOf("]") - path.IndexOf("[") - 1);
                                        
                                        if (int.TryParse(indexStr, out int index))
                                        {
                                            element = element.GetProperty(arrayName)[index];
                                        }
                                        else
                                        {
                                            throw new ArgumentException($"无效的数组索引: {path}");
                                        }
                                    }
                                    else
                                    {
                                        element = element.GetProperty(path);
                                    }
                                }
                            }

                            // 根据数据类型转换JSON值
                            ret.Value = ConvertJsonValue(element, ioarg.ValueType);
                        }
                    }
                    catch (JsonException ex)
                    {
                        ret.StatusType = VaribaleStatusTypeEnum.Bad;
                        ret.Message = $"JSON解析错误: {ex.Message}";
                        return ret;
                    }
                }
                else
                {
                    // 原有的字节解析逻辑
                    string[] parts = ioarg.Address.Split(',');
                    if (parts.Length < 2)
                    {
                        ret.StatusType = VaribaleStatusTypeEnum.Bad;
                        ret.Message = "地址格式错误，应为：起始位置,长度[,格式化参数]";
                        return ret;
                    }

                    int start = int.Parse(parts[0]);
                    int length = int.Parse(parts[1]);
                    string format = parts.Length > 2 ? parts[2] : string.Empty;

                    if (length == -1)
                    {
                        length = data.Length - start;
                    }

                    if (start + length > data.Length)
                    {
                        ret.StatusType = VaribaleStatusTypeEnum.Bad;
                        ret.Message = "数据长度超出范围";
                        return ret;
                    }

                    byte[] fieldData = new byte[length];
                    Array.Copy(data, start, fieldData, 0, length);
                    ret.Value = ParseValueByType(fieldData, ioarg.ValueType, format);
                }
            }
            catch (Exception ex)
            {
                ret.StatusType = VaribaleStatusTypeEnum.Bad;
                ret.Message = $"解析失败: {ex.Message}";
            }

            return ret;
        }

        #region IAddressDefinitionProvider Implementation
        public Dictionary<string, AddressDefinitionInfo> GetAddressDefinitions()
        {
            return TCPParserAddressDefinitions.GetDefinitions();
        }
        #endregion

        private readonly object _commandLock = new object();
        private TaskCompletionSource<byte[]> _currentCommandResponse;
        private string _currentResponsePattern;
        private System.Timers.Timer _commandTimeoutTimer;

        /// <summary>
        /// 执行命令并等待响应
        /// </summary>
        /// <param name="command">命令定义</param>
        /// <returns>命令响应</returns>
        public virtual async Task<CommandResponse> ExecuteCommandAsync(CommandDefinition command)
        {
            var startTime = DateTime.Now;
            var response = new CommandResponse();
            
            try
            {
                if (!IsConnected)
                {
                    throw new InvalidOperationException($"{ProtocolType}未连接");
                }

                for (int retry = 0; retry <= command.RetryCount; retry++)
                {
                    try
                    {
                        // 设置响应等待
                        lock (_commandLock)
                        {
                            _currentCommandResponse = new TaskCompletionSource<byte[]>();
                            _currentResponsePattern = command.ResponsePattern;
                            
                            // 设置超时定时器
                            _commandTimeoutTimer?.Dispose();
                            _commandTimeoutTimer = new System.Timers.Timer(command.Timeout);
                            _commandTimeoutTimer.Elapsed += (s, e) =>
                            {
                                _currentCommandResponse?.TrySetException(new TimeoutException("Command timeout"));
                            };
                            _commandTimeoutTimer.Start();
                        }

                        // 发送命令
                        if (ProtocolType == ProtocolTypeEnum.TCP)
                        {
                            await _stream.WriteAsync(command.CommandData);
                        }
                        else
                        {
                            await _udpClient.SendAsync(command.CommandData);
                        }

                        // 等待响应
                        var responseData = await _currentCommandResponse.Task;
                        
                        // 解析响应
                        response.Success = true;
                        response.RawData = responseData;
                        response.ParsedData = ParseCommandResponse(responseData, command.ResponsePattern);
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (retry == command.RetryCount)
                        {
                            throw;
                        }
                        _logger.LogWarning($"Command retry {retry + 1}/{command.RetryCount}: {ex.Message}");
                        await Task.Delay(command.RetryInterval);
                    }
                    finally
                    {
                        _commandTimeoutTimer?.Stop();
                    }
                }
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Error = ex.Message;
                _logger.LogError(ex, $"ExecuteCommand failed: {command.CommandId}");
            }
            finally
            {
                lock (_commandLock)
                {
                    _currentCommandResponse = null;
                    _currentResponsePattern = null;
                    _commandTimeoutTimer?.Dispose();
                    _commandTimeoutTimer = null;
                }
            }

            response.ExecutionTime = (long)(DateTime.Now - startTime).TotalMilliseconds;
            return response;
        }

        /// <summary>
        /// 解析命令响应
        /// 可以被子类重写以实现特定的解析逻辑
        /// </summary>
        protected virtual object ParseCommandResponse(byte[] responseData, string responsePattern)
        {
            if (string.IsNullOrEmpty(responsePattern))
            {
                return responseData;
            }

            try
            {
                if (responsePattern.StartsWith("json:", StringComparison.OrdinalIgnoreCase))
                {
                    // JSON解析
                    string jsonPath = responsePattern.Substring(5);
                    string jsonString = Encoding.UTF8.GetString(responseData);
                    using (JsonDocument jsonDoc = JsonDocument.Parse(jsonString))
                    {
                        JsonElement element = jsonDoc.RootElement;
                        foreach (string path in jsonPath.Split('.'))
                        {
                            if (path.Contains("[") && path.Contains("]"))
                            {
                                string arrayName = path.Substring(0, path.IndexOf("["));
                                string indexStr = path.Substring(path.IndexOf("[") + 1, path.IndexOf("]") - path.IndexOf("[") - 1);
                                if (int.TryParse(indexStr, out int index))
                                {
                                    element = element.GetProperty(arrayName)[index];
                                }
                            }
                            else
                            {
                                element = element.GetProperty(path);
                            }
                        }
                        return element;
                    }
                }
                else if (responsePattern.StartsWith("regex:", StringComparison.OrdinalIgnoreCase))
                {
                    // 正则表达式匹配
                    string pattern = responsePattern.Substring(6);
                    string responseText = Encoding.UTF8.GetString(responseData);
                    var match = System.Text.RegularExpressions.Regex.Match(responseText, pattern);
                    return match.Success ? match.Value : null;
                }
                else
                {
                    // 字节模式匹配
                    string[] expectedBytes = responsePattern.Split('-');
                    byte[] pattern = new byte[expectedBytes.Length];
                    for (int i = 0; i < expectedBytes.Length; i++)
                    {
                        pattern[i] = Convert.ToByte(expectedBytes[i], 16);
                    }
                    
                    // 查找模式
                    for (int i = 0; i <= responseData.Length - pattern.Length; i++)
                    {
                        bool found = true;
                        for (int j = 0; j < pattern.Length; j++)
                        {
                            if (responseData[i + j] != pattern[j])
                            {
                                found = false;
                                break;
                            }
                        }
                        if (found)
                        {
                            return responseData;
                        }
                    }
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Parse command response failed");
                return null;
            }
        }

        private byte[] HandleEndian(byte[] data)
        {
            if (data == null || data.Length == 0)
                return data;

            switch (EndianType)
            {
                case EndianEnum.BigEndian:
                    if (BitConverter.IsLittleEndian)
                    {
                        var tempData = new byte[data.Length];
                        Array.Copy(data, tempData, data.Length);
                        Array.Reverse(tempData);
                        return tempData;
                    }
                    break;
                case EndianEnum.LittleEndian:
                    if (!BitConverter.IsLittleEndian)
                    {
                        var tempData = new byte[data.Length];
                        Array.Copy(data, tempData, data.Length);
                        Array.Reverse(tempData);
                        return tempData;
                    }
                    break;
                case EndianEnum.BigEndianSwap:
                    var swappedData = new byte[data.Length];
                    for (int i = 0; i < data.Length; i += 2)
                    {
                        if (i + 1 < data.Length)
                        {
                            swappedData[i] = data[i + 1];
                            swappedData[i + 1] = data[i];
                        }
                        else
                        {
                            swappedData[i] = data[i];
                        }
                    }
                    if (BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(swappedData);
                    }
                    return swappedData;
                case EndianEnum.LittleEndianSwap:
                    var swappedDataLE = new byte[data.Length];
                    for (int i = 0; i < data.Length; i += 2)
                    {
                        if (i + 1 < data.Length)
                        {
                            swappedDataLE[i] = data[i + 1];
                            swappedDataLE[i + 1] = data[i];
                        }
                        else
                        {
                            swappedDataLE[i] = data[i];
                        }
                    }
                    if (!BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(swappedDataLE);
                    }
                    return swappedDataLE;
            }
            
            return data;
        }

        private object ParseValueByType(byte[] data, DataTypeEnum valueType, string format)
        {
            var processedData = HandleEndian(data);
            switch (valueType)
            {
                case DataTypeEnum.Bool:
                    return processedData[0] != 0;

                case DataTypeEnum.Byte:
                    return processedData[0];

                case DataTypeEnum.Int16:
                    return BitConverter.ToInt16(processedData, 0);

                case DataTypeEnum.Uint16:
                    return BitConverter.ToUInt16(processedData, 0);

                case DataTypeEnum.Int32:
                    return BitConverter.ToInt32(processedData, 0);

                case DataTypeEnum.Uint32:
                    return BitConverter.ToUInt32(processedData, 0);

                case DataTypeEnum.Int64:
                    return BitConverter.ToInt64(processedData, 0);

                case DataTypeEnum.Uint64:
                    return BitConverter.ToUInt64(processedData, 0);

                case DataTypeEnum.Float:
                    float floatValue = BitConverter.ToSingle(processedData, 0);
                    if (!string.IsNullOrEmpty(format))
                    {
                        return Math.Round(floatValue, int.Parse(format));
                    }
                    return floatValue;

                case DataTypeEnum.Double:
                    double doubleValue = BitConverter.ToDouble(processedData, 0);
                    if (!string.IsNullOrEmpty(format))
                    {
                        return Math.Round(doubleValue, int.Parse(format));
                    }
                    return doubleValue;

                case DataTypeEnum.AsciiString:
                    return Encoding.ASCII.GetString(processedData).TrimEnd('\0');

                case DataTypeEnum.Utf8String:
                    return Encoding.UTF8.GetString(processedData).TrimEnd('\0');

                case DataTypeEnum.DateTime:
                    long ticks = BitConverter.ToInt64(processedData, 0);
                    return DateTime.FromFileTime(ticks);

                default:
                    throw new ArgumentException($"不支持的数据类型: {valueType}");
            }
        }

        private object ConvertJsonValue(JsonElement element, DataTypeEnum valueType)
        {
            switch (valueType)
            {
                case DataTypeEnum.Bool:
                    return element.GetBoolean();

                case DataTypeEnum.Byte:
                    return (byte)element.GetInt32();

                case DataTypeEnum.Int16:
                    return (short)element.GetInt32();

                case DataTypeEnum.Uint16:
                    return (ushort)element.GetInt32();

                case DataTypeEnum.Int32:
                    return element.GetInt32();

                case DataTypeEnum.Uint32:
                    return (uint)element.GetInt64();

                case DataTypeEnum.Int64:
                    return element.GetInt64();

                case DataTypeEnum.Uint64:
                    return (ulong)element.GetInt64();

                case DataTypeEnum.Float:
                    return (float)element.GetDouble();

                case DataTypeEnum.Double:
                    return element.GetDouble();

                case DataTypeEnum.AsciiString:
                case DataTypeEnum.Utf8String:
                    return element.GetString();

                case DataTypeEnum.DateTime:
                    return element.GetString();


                default:
                    throw new ArgumentException($"不支持的JSON数据类型转换: {valueType}");
            }
        }

        public async Task<RpcResponse> WriteAsync(string requestId, string method, DriverAddressIoArgModel ioarg)
        {
            try
            {
                // Convert the ioarg to byte array based on its data type
                byte[] data = ConvertToBytes(ioarg);
                
                if (ProtocolType == ProtocolTypeEnum.TCP)
                {
                    if (_stream == null)
                        return new RpcResponse { IsSuccess = false, Description = "TCP stream not connected" };
                        
                    await _stream.WriteAsync(data);
                }
                else // UDP
                {
                    if (_udpClient == null)
                        return new RpcResponse { IsSuccess = false, Description = "UDP client not connected" };
                        
                    await _udpClient.SendAsync(data);
                }
                
                return new RpcResponse { IsSuccess = true, Description = "Data sent successfully" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Device:[{_device}],WriteAsync(),Error");
                return new RpcResponse { IsSuccess = false, Description = ex.Message };
            }
        }

        private byte[] ConvertToBytes(DriverAddressIoArgModel ioarg)
        {
            // Parse address format similar to Read method
            string[] parts = ioarg.Address.Split(',');
            if (parts.Length < 2)
            {
                throw new ArgumentException("地址格式错误，应为：起始位置,长度[,格式化参数]");
            }

            // Convert the value to bytes based on the data type
            switch (ioarg.ValueType)
            {
                case DataTypeEnum.Bool:
                    return new byte[] { Convert.ToBoolean(ioarg.Value) ? (byte)1 : (byte)0 };

                case DataTypeEnum.Byte:
                    return new byte[] { Convert.ToByte(ioarg.Value) };

                case DataTypeEnum.Int16:
                    return BitConverter.GetBytes(Convert.ToInt16(ioarg.Value));

                case DataTypeEnum.Uint16:
                    return BitConverter.GetBytes(Convert.ToUInt16(ioarg.Value));

                case DataTypeEnum.Int32:
                    return BitConverter.GetBytes(Convert.ToInt32(ioarg.Value));

                case DataTypeEnum.Uint32:
                    return BitConverter.GetBytes(Convert.ToUInt32(ioarg.Value));

                case DataTypeEnum.Int64:
                    return BitConverter.GetBytes(Convert.ToInt64(ioarg.Value));

                case DataTypeEnum.Uint64:
                    return BitConverter.GetBytes(Convert.ToUInt64(ioarg.Value));

                case DataTypeEnum.Float:
                    return BitConverter.GetBytes(Convert.ToSingle(ioarg.Value));

                case DataTypeEnum.Double:
                    return BitConverter.GetBytes(Convert.ToDouble(ioarg.Value));

                case DataTypeEnum.AsciiString:
                    string format = parts.Length > 2 ? parts[2].ToLower() : string.Empty;
                    if (format == "hex")
                    {
                        string hexString = ioarg.Value.ToString().Replace("-", "");
                        byte[] bytes = new byte[hexString.Length / 2];
                        for (int i = 0; i < bytes.Length; i++)
                        {
                            bytes[i] = Convert.ToByte(hexString.Substring(i * 2, 2), 16);
                        }
                        return bytes;
                    }
                    else if (format == "utf8")
                    {
                        return Encoding.UTF8.GetBytes(ioarg.Value.ToString());
                    }
                    return Encoding.ASCII.GetBytes(ioarg.Value.ToString());

                case DataTypeEnum.Utf8String:
                    return Encoding.UTF8.GetBytes(ioarg.Value.ToString());

                case DataTypeEnum.DateTime:
                    DateTime dt = Convert.ToDateTime(ioarg.Value);
                    long timestamp = ((DateTimeOffset)dt).ToUnixTimeSeconds();
                    return BitConverter.GetBytes(timestamp);

                default:
                    throw new ArgumentException($"不支持的数据类型: {ioarg.ValueType}");
            }
        }
    }
}
