using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PluginInterface;

namespace HTTP.JsonClient
{
    [DriverSupported("HTTP.JsonClient")]
    [DriverInfo("HTTP.JsonClient", "1.0.0", "HTTP JSON Client Driver")]
    public class HttpJsonClientDriver : IDriver
    {
        private readonly HttpClient _httpClient;
        private bool _isConnected;
        private JObject _lastData;
        private DateTime _lastUpdateTime;

        [ConfigParameter("DeviceId")]
        public string DeviceId { get; set; }

        [ConfigParameter("BaseUrl")]
        public string BaseUrl { get; set; }

        [ConfigParameter("Path")]
        public string Path { get; set; }

        [ConfigParameter("UpdateInterval", Optional = true)]
        public int UpdateInterval { get; set; } = 1000; // Default 1 second

        [ConfigParameter("Timeout", Optional = true)]
        public int Timeout { get; set; } = 3000; // Default 3 seconds

        [ConfigParameter("Headers", Optional = true)]
        public string Headers { get; set; }

        public bool IsConnected => _isConnected;
        public uint MinPeriod => (uint)UpdateInterval;
        public ILogger _logger { get; set; }

        public HttpJsonClientDriver()
        {
            _httpClient = new HttpClient();
            _isConnected = false;
            _lastData = null;
            _lastUpdateTime = DateTime.MinValue;
        }

        public bool Connect()
        {
            try
            {
                _httpClient.BaseAddress = new Uri(BaseUrl);
                _httpClient.Timeout = TimeSpan.FromMilliseconds(Timeout);
                
                // Add headers if specified
                if (!string.IsNullOrEmpty(Headers))
                {
                    try
                    {
                        var headerDict = JsonConvert.DeserializeObject<Dictionary<string, string>>(Headers);
                        foreach (var header in headerDict)
                        {
                            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Failed to parse headers JSON");
                        return false;
                    }
                }

                _isConnected = true;
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to connect to HTTP endpoint");
                _isConnected = false;
                return false;
            }
        }

        public bool Close()
        {
            try
            {
                _isConnected = false;
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error closing connection");
                return false;
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
            GC.SuppressFinalize(this);
        }

        private async Task UpdateDataAsync()
        {
            try
            {
                if ((DateTime.Now - _lastUpdateTime).TotalMilliseconds < UpdateInterval)
                {
                    return;
                }

                var response = await _httpClient.GetAsync(Path);
                response.EnsureSuccessStatusCode();
                var jsonString = await response.Content.ReadAsStringAsync();
                _lastData = JObject.Parse(jsonString);
                _lastUpdateTime = DateTime.Now;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error updating data from HTTP endpoint");
                _isConnected = false;
                throw;
            }
        }

        public DriverReturnValueModel Read(DriverAddressIoArgModel ioArg)
        {
            try
            {
                UpdateDataAsync().Wait();

                if (_lastData == null)
                {
                    return new DriverReturnValueModel
                    {
                        Value = null,
                        StatusType = VaribaleStatusTypeEnum.Bad
                    };
                }

                // JSONPath 语法示例:
                // $ - 根节点 - 获取整个JSON数据
                // $.store - 获取store节点
                // $.store.book - 获取store节点下的book节点
                // $.store.book[0].title - 获取第一本书的标题
                // $.store.book[*].author - 获取所有书的作者
                // $.store.book[?(@.price < 10)].title - 获取价格小于10的书的标题
                // $.store.bicycle.color - 获取自行车的颜色
                // $.store.book[-1].title - 获取最后一本书的标题
                // $.store.book[0:2].title - 获取前两本书的标题
                var token = _lastData.SelectToken(ioArg.Address);
                
                if (token == null)
                {
                    _logger?.LogWarning($"Path '{ioArg.Address}' not found in JSON data");
                    return new DriverReturnValueModel
                    {
                        Value = null,
                        StatusType = VaribaleStatusTypeEnum.Bad
                    };
                }

                // 根据数据类型转换值
                object value;
                switch (ioArg.ValueType)
                {
                    case DataTypeEnum.Int16:
                    case DataTypeEnum.Int32:
                    case DataTypeEnum.Int64:
                        value = token.Value<long>();
                        break;
                    case DataTypeEnum.UInt16:
                    case DataTypeEnum.UInt32:
                    case DataTypeEnum.UInt64:
                        value = token.Value<ulong>();
                        break;
                    case DataTypeEnum.Float:
                    case DataTypeEnum.Double:
                        value = token.Value<double>();
                        break;
                    case DataTypeEnum.Boolean:
                        value = token.Value<bool>();
                        break;
                    case DataTypeEnum.String:
                        value = token.Value<string>();
                        break;
                    default:
                        value = token.ToObject<object>();
                        break;
                }

                return new DriverReturnValueModel
                {
                    Value = value,
                    StatusType = VaribaleStatusTypeEnum.Good
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error reading data");
                return new DriverReturnValueModel
                {
                    Value = null,
                    StatusType = VaribaleStatusTypeEnum.Bad
                };
            }
        }

        public async Task<RpcResponse> WriteAsync(string RequestId, string Method, DriverAddressIoArgModel ioArg)
        {
            // Write operation is not supported in this read-only driver
            return await Task.FromResult(new RpcResponse
            {
                IsSuccess = false,
                Description = "Write operation is not supported in HTTP JSON Client driver"
            });
        }
    }
}
