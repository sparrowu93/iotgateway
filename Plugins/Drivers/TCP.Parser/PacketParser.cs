using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PluginInterface;
using System.Text.Json;

namespace TCP.Parser
{
    [DriverSupported("TCPParser")]
    [DriverInfo("TCPParser", "V1.0.0", "Copyright IoTGateway 2024-12-26")]
    public class TCPParser : IDriver
    {
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        private bool _isRunning;
        private byte[] _latestData;
        public ILogger _logger { get; set; }
        private readonly string _device;

        #region 配置参数
        [ConfigParameter("设备Id")] 
        public string DeviceId { get; set; }

        [ConfigParameter("IP地址")] 
        public string IpAddress { get; set; } = "127.0.0.1";

        [ConfigParameter("端口号")] 
        public int Port { get; set; } = 502;

        [ConfigParameter("超时时间ms")] 
        public int Timeout { get; set; } = 3000;

        [ConfigParameter("最小通讯周期ms")] 
        public uint MinPeriod { get; set; } = 100;

        [ConfigParameter("报文格式定义")] 
        public string PacketFormat { get; set; } = @"{
    ""fields"": [
        {""name"": ""header"", ""start"": 0, ""length"": 2, ""type"": ""hex""},
        {""name"": ""length"", ""start"": 2, ""length"": 2, ""type"": ""int""},
        {""name"": ""data"", ""start"": 4, ""length"": -1, ""type"": ""string""}
    ]
}";
        #endregion

        public TCPParser(string device, ILogger logger)
        {
            _device = device;
            _logger = logger;
            _latestData = Array.Empty<byte>();
            _logger.LogInformation($"Device:[{_device}],Create()");
        }

        public bool IsConnected => _tcpClient?.Connected ?? false;

        public bool Connect()
        {
            try
            {
                _logger.LogInformation($"Device:[{_device}],Connect()");
                _tcpClient = new TcpClient();
                _tcpClient.ReceiveTimeout = Timeout;
                _tcpClient.SendTimeout = Timeout;
                _tcpClient.Connect(IpAddress, Port);
                _stream = _tcpClient.GetStream();
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
                _stream?.Close();
                _tcpClient?.Close();
                _tcpClient = null;
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
            _tcpClient?.Dispose();
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
            byte[] buffer = new byte[1024];
            while (_isRunning)
            {
                try
                {
                    if (_stream?.DataAvailable == true)
                    {
                        int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
                        if (bytesRead > 0)
                        {
                            _latestData = new byte[bytesRead];
                            Array.Copy(buffer, _latestData, bytesRead);
                            _logger.LogDebug($"Received data: {BitConverter.ToString(_latestData)}");
                        }
                    }
                    await Task.Delay((int)MinPeriod);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Device:[{_device}],ReceiveData(),Error");
                    await Task.Delay(1000);
                }
            }
        }

        [Method("读取字段值", description: "读取指定字段的值")]
        public DriverReturnValueModel Read(DriverAddressIoArgModel ioarg)
        {
            var ret = new DriverReturnValueModel { StatusType = VaribaleStatusTypeEnum.Good };

            if (IsConnected && _latestData.Length > 0)
            {
                try
                {
                    // 解析地址格式：起始位置,长度[,格式化参数]
                    // 示例：
                    // "0,2" - 从位置0读取2个字节
                    // "4,-1" - 从位置4读取到末尾
                    // "0,4,hex" - 从位置0读取4个字节并格式化为十六进制
                    // "2,4,0.00" - 从位置2读取4个字节的浮点数并格式化为2位小数
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

                    // 如果长度为-1，使用剩余所有数据
                    if (length == -1)
                    {
                        length = _latestData.Length - start;
                    }

                    // 检查边界
                    if (start + length > _latestData.Length)
                    {
                        ret.StatusType = VaribaleStatusTypeEnum.Bad;
                        ret.Message = "数据长度超出范围";
                        return ret;
                    }

                    // 提取数据
                    byte[] fieldData = new byte[length];
                    Array.Copy(_latestData, start, fieldData, 0, length);

                    // 根据 ValueType 和格式化参数解析数据
                    ret.Value = ParseValueByType(fieldData, ioarg.ValueType, format);
                }
                catch (Exception ex)
                {
                    ret.StatusType = VaribaleStatusTypeEnum.Bad;
                    ret.Message = $"解析失败: {ex.Message}";
                }
            }
            else
            {
                ret.StatusType = VaribaleStatusTypeEnum.Bad;
                ret.Message = IsConnected ? "无数据" : "TCP未连接";
            }

            return ret;
        }

        private object ParseValueByType(byte[] data, DataTypeEnum valueType, string format)
        {
            try
            {
                switch (valueType)
                {
                    case DataTypeEnum.Bool:
                        return data[0] != 0;

                    case DataTypeEnum.Byte:
                        return data[0];

                    case DataTypeEnum.Short:
                        return BitConverter.ToInt16(data, 0);

                    case DataTypeEnum.UShort:
                        return BitConverter.ToUInt16(data, 0);

                    case DataTypeEnum.Int:
                        return BitConverter.ToInt32(data, 0);

                    case DataTypeEnum.UInt:
                        return BitConverter.ToUInt32(data, 0);

                    case DataTypeEnum.Long:
                        return BitConverter.ToInt64(data, 0);

                    case DataTypeEnum.ULong:
                        return BitConverter.ToUInt64(data, 0);

                    case DataTypeEnum.Float:
                        float floatValue = BitConverter.ToSingle(data, 0);
                        if (!string.IsNullOrEmpty(format))
                        {
                            return Math.Round(floatValue, int.Parse(format));
                        }
                        return floatValue;

                    case DataTypeEnum.Double:
                        double doubleValue = BitConverter.ToDouble(data, 0);
                        if (!string.IsNullOrEmpty(format))
                        {
                            return Math.Round(doubleValue, int.Parse(format));
                        }
                        return doubleValue;

                    case DataTypeEnum.String:
                        if (format?.ToLower() == "hex")
                        {
                            return BitConverter.ToString(data);
                        }
                        else if (format?.ToLower() == "utf8")
                        {
                            return Encoding.UTF8.GetString(data);
                        }
                        return Encoding.ASCII.GetString(data);

                    case DataTypeEnum.ByteArray:
                        return data;

                    case DataTypeEnum.DateTime:
                        // 假设数据是Unix时间戳（秒）
                        long timestamp = BitConverter.ToInt64(data, 0);
                        return DateTimeOffset.FromUnixTimeSeconds(timestamp).LocalDateTime;

                    default:
                        throw new ArgumentException($"不支持的数据类型: {valueType}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"解析{valueType}类型数据失败: {ex.Message}");
            }
        }

        public async Task<RpcResponse> WriteAsync(string requestId, string method, DriverAddressIoArgModel ioarg)
        {
            RpcResponse rpcResponse = new() { IsSuccess = false, Description = "设备驱动内未实现写入功能" };
            return rpcResponse;
        }
    }
}
