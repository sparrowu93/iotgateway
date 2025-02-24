using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PluginInterface;
using WeightBalance.Models;

namespace WeightBalance
{
    /// <summary>
    /// 称重定重心台驱动
    /// 支持两个服务：
    /// 1. 设备服务器（192.168.1.200:204）
    /// 2. 测试设备服务器（192.168.1.180:402）
    /// </summary>
    [DriverSupported("WeightBalance")]
    [DriverInfo("WeightBalance", "V1.0.0", "Copyright IoTGateway 2024-12-26")]
    public class WeightBalanceDriver : IDriver
    {
        private readonly ILogger _logger;
        private readonly string _device;
        private byte[] _latestData = Array.Empty<byte>();
        private bool _isConnected;

        private const string PRODUCT_TYPE_MPTA5000 = "MPTA-5000";
        private const string SERVICE_TEST_COMMAND = "START_TEST";

        #region 配置参数
        [ConfigParameter("设备Id")]
        public string DeviceId { get; set; } = string.Empty;

        [ConfigParameter("协议类型")]
        public string ProtocolType { get; set; } = "TCP";

        [ConfigParameter("IP地址")]
        public string IpAddress { get; set; } = "192.168.1.200";

        [ConfigParameter("端口号")]
        public int Port { get; set; } = 204;

        [ConfigParameter("超时时间ms")]
        public int Timeout { get; set; } = 3000;

        [ConfigParameter("命令重试次数")]
        public int CommandRetries { get; set; } = 3;

        [ConfigParameter("命令重试间隔ms")]
        public int RetryInterval { get; set; } = 1000;
        #endregion

        public WeightBalanceDriver(string device, ILogger logger)
        {
            _device = device;
            _logger = logger;
            _logger.LogInformation($"Device:[{_device}],Create()");
        }

        public bool IsConnected => _isConnected;

        public bool Connect()
        {
            try
            {
                _logger.LogInformation($"Device:[{_device}],Connect()");
                _isConnected = true;
                return true;
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
                _isConnected = false;
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
        }

        /// <summary>
        /// 测试服务状态
        /// </summary>
        public async Task<WeightBalanceTestResult> TestServiceStatusAsync()
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("设备未连接");
            }

            try
            {
                var testCommand = $"cmd:{SERVICE_TEST_COMMAND}:MPTA-5000:1:无测试任务";
                var response = await SendCommandAsync(testCommand);
                
                return new WeightBalanceTestResult 
                { 
                    IsServiceAvailable = !string.IsNullOrEmpty(response) && 
                                       response.StartsWith(PRODUCT_TYPE_MPTA5000) 
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "测试服务状态失败");
                return new WeightBalanceTestResult { IsServiceAvailable = false };
            }
        }

        [Method("读取", description: "读取定重心台参数")]
        public DriverReturnValueModel Read(DriverAddressIoArgModel ioarg)
        {
            return TestRead(ioarg);
        }

        public DriverReturnValueModel TestRead(DriverAddressIoArgModel ioarg)
        {
            var ret = new DriverReturnValueModel { StatusType = VaribaleStatusTypeEnum.Good };

            try
            {
                // 处理特定的测试命令
                if (ioarg.Address.StartsWith("cmd:"))
                {
                    var parts = ioarg.Address.Split(':');
                    if (parts.Length >= 4 && parts[1] == SERVICE_TEST_COMMAND)
                    {
                        // 构建响应格式：MPTA-5000:2:"产品可号":"产品编号":"型号状态"
                        ret.Value = $"{PRODUCT_TYPE_MPTA5000}:2:\"{parts[2]}\":\"{parts[3]}\":\"测试中\"";
                        return ret;
                    }
                }

                // 处理JSON数据解析
                if (ioarg.Address.StartsWith("json:"))
                {
                    string jsonPath = ioarg.Address.Substring(5);
                    if (_latestData.Length == 0)
                    {
                        ret.StatusType = VaribaleStatusTypeEnum.Bad;
                        ret.Message = "无数据";
                        return ret;
                    }

                    try
                    {
                        string jsonString = Encoding.UTF8.GetString(_latestData);
                        using var jsonDoc = JsonDocument.Parse(jsonString);
                        var element = jsonDoc.RootElement;
                        foreach (string path in jsonPath.Split('.'))
                        {
                            element = element.GetProperty(path);
                        }
                        ret.Value = ConvertJsonValue(element, ioarg.ValueType);
                        return ret;
                    }
                    catch (Exception ex)
                    {
                        ret.StatusType = VaribaleStatusTypeEnum.Bad;
                        ret.Message = $"JSON解析错误: {ex.Message}";
                        return ret;
                    }
                }

                ret.StatusType = VaribaleStatusTypeEnum.Bad;
                ret.Message = "不支持的地址格式";
                return ret;
            }
            catch (Exception ex)
            {
                ret.StatusType = VaribaleStatusTypeEnum.Bad;
                ret.Message = $"测试读取失败: {ex.Message}";
                return ret;
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
                    return element.GetString() ?? string.Empty;

                case DataTypeEnum.DateTime:
                    return element.GetString() ?? string.Empty;

                default:
                    throw new ArgumentException($"不支持的数据类型转换: {valueType}");
            }
        }

        private async Task<string> SendCommandAsync(string command)
        {
            // 这里实现实际的命令发送逻辑
            // 在实际项目中，需要根据具体的通信协议实现
            await Task.Delay(100); // 模拟网络延迟
            return $"{PRODUCT_TYPE_MPTA5000}:2:\"TEST\":\"001\":\"测试中\"";
        }

        public Task<RpcResponse> WriteAsync(string requestId, string method, DriverAddressIoArgModel ioarg)
        {
            // 实现写入功能
            return Task.FromResult(new RpcResponse { IsSuccess = true });
        }
    }
}
