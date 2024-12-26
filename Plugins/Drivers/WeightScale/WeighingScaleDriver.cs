using System.IO.Ports;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Text;
using Microsoft.Extensions.Logging;
using PluginInterface;
using TCP.Parser;

namespace WeightScale
{
    [DriverSupported("WeighingScaleDriver")]
    [DriverInfo("WeighingScaleDriver", "V1.0.0", "Copyright IoTGateway 2024-03-17")]
    public class WeighingScaleDriver : IDriver
    {
        private SerialPort? _serialPort;
        private TcpClient? _tcpClient;
        private NetworkStream? _tcpStream;
        private string _latestWeight = "0";
        public ILogger _logger { get; set; }
        private readonly string _device;
        private bool _isRunning = false;
        private BasePacketParser? _parser;

        #region 配置参数

        [ConfigParameter("设备Id")] public string DeviceId { get; set; }

        [ConfigParameter("通信模式")] public CommunicationMode Mode { get; set; } = CommunicationMode.Serial;

        // 串口参数
        [ConfigParameter("串口名")] public string PortName { get; set; } = "COM1";
        [ConfigParameter("波特率")] public int BaudRate { get; set; } = 9600;
        [ConfigParameter("数据位")] public int DataBits { get; set; } = 8;
        [ConfigParameter("停止位")] public StopBits StopBits { get; set; } = StopBits.One;
        [ConfigParameter("校验位")] public Parity Parity { get; set; } = Parity.None;

        // TCP参数
        [ConfigParameter("IP地址")] public string IpAddress { get; set; } = "127.0.0.1";
        [ConfigParameter("端口号")] public int Port { get; set; } = 8080;

        [ConfigParameter("数据格式")] 
        public WeightFormat Format { get; set; } = WeightFormat.TF0;

        [ConfigParameter("重量单位")] 
        public WeightUnit Unit { get; set; } = WeightUnit.Kilogram;

        [ConfigParameter("超时时间ms")] public int Timeout { get; set; } = 1000;
        [ConfigParameter("最小通讯周期ms")] public uint MinPeriod { get; set; } = 100;

        #endregion

        public WeighingScaleDriver(string device, ILogger logger)
        {
            _device = device;
            _logger = logger;

            _logger.LogInformation($"Device:[{_device}],Create()");
        }

        public bool IsConnected
        {
            get
            {
                return Mode == CommunicationMode.Serial
                    ? (_serialPort != null && _serialPort.IsOpen)
                    : (_tcpClient != null && _tcpClient.Connected);
            }
        }

        public bool Connect()
        {
            try
            {
                if (Mode == CommunicationMode.Serial)
                {
                    _serialPort = new SerialPort(PortName, BaudRate, Parity, DataBits, StopBits)
                    {
                        ReadTimeout = Timeout,
                        WriteTimeout = Timeout
                    };
                    _serialPort.DataReceived += SerialPort_DataReceived;
                    _serialPort.Open();
                }
                else
                {
                    _tcpClient = new TcpClient();
                    _tcpClient.Connect(IpAddress, Port);
                    _tcpStream = _tcpClient.GetStream();
                    _tcpStream.ReadTimeout = Timeout;

                    // 根据选择的格式初始化解析器
                    string packetFormat = GetPacketFormat(Format);
                    if (!string.IsNullOrEmpty(packetFormat))
                    {
                        _parser = new BasePacketParser(packetFormat);
                    }

                    _isRunning = true;
                    Task.Run(ListenForTcpData);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"连接失败: {ex.Message}");
                return false;
            }

            return IsConnected;
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (_serialPort == null) return;

            try
            {
                string data = _serialPort.ReadLine();
                ProcessReceivedData(data);
            }
            catch (Exception ex)
            {
                _logger.LogError($"读取串口数据失败: {ex.Message}");
            }
        }

        private async Task ListenForTcpData()
        {
            byte[] buffer = new byte[1024];
            while (_isRunning)
            {
                try
                {
                    if (_tcpStream != null && _tcpStream.CanRead)
                    {
                        int bytesRead = await _tcpStream.ReadAsync(buffer, 0, buffer.Length);
                        if (bytesRead > 0)
                        {
                            byte[] data = new byte[bytesRead];
                            Array.Copy(buffer, data, bytesRead);

                            if (_parser != null)
                            {
                                var parsedData = _parser.Parse(data);
                                if (parsedData.TryGetValue("weight", out var weightObj))
                                {
                                    string weightStr = weightObj.ToString();
                                    ProcessReceivedData(weightStr);
                                }
                            }
                            else
                            {
                                string asciiData = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                                ProcessReceivedData(asciiData);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"读取TCP数据失败: {ex.Message}");
                    await Task.Delay(1000);
                }
            }
        }

        private void ProcessReceivedData(string data)
        {
            try
            {
                float? weight = ParseContinuousData(data);
                if (weight.HasValue)
                {
                    _latestWeight = weight.Value.ToString("F6");
                    _logger.LogInformation($"接收到新的重量数据: {_latestWeight} kg");
                }
                else
                {
                    _logger.LogWarning("无法解析接收到的数据");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"处理数据时发生错误: {ex.Message}");
            }
        }

        private float? ParseContinuousData(string data)
        {
            // 尝试TF0格式
            var tf0Match = Regex.Match(data, @"\x02([+-]\d{9})\x03");
            if (tf0Match.Success)
            {
                _logger.LogInformation("检测到TF0格式数据");
                return ParseTF0(data);
            }

            // 尝试TF2格式
            var tf2Match = Regex.Match(data, @"([-+]?\d+\.\d{5})=");
            if (tf2Match.Success)
            {
                _logger.LogInformation("检测到TF2格式数据");
                return ParseTF2(data);
            }

            // 尝试TF3格式
            var tf3Match = Regex.Match(data, @"([-+]?\d+\.\d{6})=");
            if (tf3Match.Success)
            {
                _logger.LogInformation("检测到TF3格式数据");
                return ParseTF3(data);
            }

            _logger.LogWarning("无法识别的数据格式");
            return null;
        }

        private float? ParseTF0(string data)
        {
            var match = Regex.Match(data, @"\x02([+-]\d{9})\x03");
            if (match.Success)
            {
                string weightStr = match.Groups[1].Value;
                if (float.TryParse(weightStr, out float weight))
                {
                    return weight / 1000; // 假设原始数据单位为g，转换为kg
                }
            }
            return null;
        }

        private float? ParseTF2(string data)
        {
            var match = Regex.Match(data, @"([-+]?\d+\.\d{5})=");
            if (match.Success)
            {
                string weightStr = match.Groups[1].Value;
                if (float.TryParse(weightStr, out float weight))
                {
                    return weight;
                }
            }
            return null;
        }

        private float? ParseTF3(string data)
        {
            var match = Regex.Match(data, @"([-+]?\d+\.\d{6})=");
            if (match.Success)
            {
                string weightStr = match.Groups[1].Value;
                if (float.TryParse(weightStr, out float weight))
                {
                    return weight;
                }
            }
            return null;
        }

        private string GetPacketFormat(WeightFormat format)
        {
            switch (format)
            {
                case WeightFormat.TF0:
                    return @"{
                        ""fields"": [
                            {""name"": ""stx"", ""start"": 0, ""length"": 1, ""type"": ""hex""},
                            {""name"": ""weight"", ""start"": 1, ""length"": 9, ""type"": ""string""},
                            {""name"": ""etx"", ""start"": 10, ""length"": 1, ""type"": ""hex""}
                        ]
                    }";
                case WeightFormat.TF2:
                case WeightFormat.TF3:
                    return @"{
                        ""fields"": [
                            {""name"": ""weight"", ""start"": 0, ""length"": -1, ""type"": ""string""}
                        ]
                    }";
                default:
                    return null;
            }
        }

        private float ConvertWeight(float weight)
        {
            switch (Unit)
            {
                case WeightUnit.Gram:
                    return weight * 1000;
                case WeightUnit.Pound:
                    return weight * 2.20462f;
                case WeightUnit.Ounce:
                    return weight * 35.274f;
                default: // Kilogram
                    return weight;
            }
        }

        public bool Close()
        {
            try
            {
                if (Mode == CommunicationMode.Serial)
                {
                    if (_serialPort != null)
                    {
                        _serialPort.DataReceived -= SerialPort_DataReceived;
                        _serialPort.Close();
                        _serialPort = null;
                    }
                }
                else
                {
                    _isRunning = false;
                    _tcpStream?.Close();
                    _tcpClient?.Close();
                    _tcpClient = null;
                    _tcpStream = null;
                }

                return !IsConnected;
            }
            catch (Exception ex)
            {
                _logger.LogError($"关闭连接失败: {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            Close();
            _serialPort?.Dispose();
            _tcpClient?.Dispose();
        }

        [Method("读取地磅重量", description: "读取当前地磅重量")]
        public DriverReturnValueModel Read(DriverAddressIoArgModel ioarg)
        {
            var ret = new DriverReturnValueModel { StatusType = VaribaleStatusTypeEnum.Good };

            if (IsConnected)
            {
                try
                {
                    if (float.TryParse(_latestWeight, out float weight))
                    {
                        ret.Value = ConvertWeight(weight);
                    }
                    else
                    {
                        ret.StatusType = VaribaleStatusTypeEnum.Bad;
                        ret.Message = "无法解析重量数据";
                    }
                }
                catch (Exception ex)
                {
                    ret.StatusType = VaribaleStatusTypeEnum.Bad;
                    ret.Message = $"读取失败,{ex.Message}";
                }
            }
            else
            {
                ret.StatusType = VaribaleStatusTypeEnum.Bad;
                ret.Message = Mode == CommunicationMode.Serial ? "串口未连接" : "TCP未连接";
            }

            return ret;
        }

        public async Task<RpcResponse> WriteAsync(string requestId, string method, DriverAddressIoArgModel ioarg)
        {
            RpcResponse rpcResponse = new() { IsSuccess = false, Description = "设备驱动内未实现写入功能" };
            return rpcResponse;
        }
    }

    public enum WeightUnit
    {
        Kilogram,
        Gram,
        Pound,
        Ounce
    }

    public enum WeightFormat
    {
        TF0,
        TF2,
        TF3
    }

    public enum CommunicationMode
    {
        Serial,
        TCP
    }
}