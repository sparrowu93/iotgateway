using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PluginInterface;
using HTTP.JsonClient.Models;
using System.ComponentModel;
using System.Text;
using System.Threading;
using System.Linq;
using System.Collections.Generic;

namespace HTTP.JsonClient
{
    [DriverSupported("HTTP.JsonClient")]
    [DriverInfo("HTTP.JsonClient", "1.0.0", "HTTP JSON Client Driver")]
    public class HttpJsonClientDriver : IDriver, IAddressDefinitionProvider
    {
        private HttpClient _httpClient;
        private bool _isConnected;
        private JObject _lastData;
        private DateTime _lastUpdateTime;
        private readonly object _lock = new object();
        private bool _isDisposed;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        #region 配置参数

        [ConfigParameter("DeviceId")]
        public string DeviceId { get; set; }

        [ConfigParameter("主机地址")]
        public string BaseUrl { get; set; }

        [ConfigParameter("Path")]
        public string Path { get; set; }

        [ConfigParameter("更新间隔")]
        public int UpdateInterval { get; set; } = 1000; // Default 1 second

        [ConfigParameter("Timeout")]
        public int Timeout { get; set; } = 3000; // Default 3 seconds

        [ConfigParameter("Headers,k=v;k1=v1")]
        public string Headers { get; set; }

        [ConfigParameter("查询参数,k=v;k1=v1")]
        public string QueryParams { get; set; } = ""; // For URL query parameters

        [ConfigParameter("请求体")]
        public string Body { get; set; } = ""; // For request body content

        [ConfigParameter("请求方法")]
        public HttpMethod Method { get; set; } = HttpMethod.GET;

        [ConfigParameter("内容类型")]
        public ContentType ContentType { get; set; } = ContentType.Json;

        #endregion


        public bool IsConnected => _isConnected;
        public uint MinPeriod => (uint)UpdateInterval;
        public ILogger _logger { get; set; }

        public HttpJsonClientDriver(string device, ILogger logger)
        {
            DeviceId = device;
            _isConnected = false;
            _lastData = null;
            _lastUpdateTime = DateTime.MinValue;
            _logger = logger;
            _isDisposed = false;
        }

        public bool Connect()
        {
            try
            {
                if (_isDisposed) return false;
                
                // Use a timeout for semaphore to prevent deadlocks
                if (!_semaphore.Wait(TimeSpan.FromSeconds(5)))
                {
                    _logger?.LogError("Timeout waiting for semaphore lock during Connect");
                    return false;
                }
                
                try
                {
                    if (_isDisposed) return false;
                    
                    // Dispose old client and create a new one
                    if (_httpClient != null)
                    {
                        _httpClient.Dispose();
                    }
                    
                    // Create a new HttpClient instance
                    _httpClient = new HttpClient();
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
                finally
                {
                    try { _semaphore.Release(); } 
                    catch (ObjectDisposedException) { /* Ignore if already disposed */ }
                }
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
                if (_isDisposed) return false;
                
                if (!_semaphore.Wait(TimeSpan.FromSeconds(5)))
                {
                    _logger?.LogError("Timeout waiting for semaphore lock during Close");
                    return false;
                }
                
                try
                {
                    if (_isDisposed) return false;
                    _isConnected = false;
                    return true;
                }
                finally
                {
                    try { _semaphore.Release(); } 
                    catch (ObjectDisposedException) { /* Ignore if already disposed */ }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error closing connection");
                return false;
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            
            // Use a timeout to prevent deadlock
            if (!_semaphore.Wait(TimeSpan.FromSeconds(5)))
            {
                _logger?.LogError("Timeout waiting for semaphore lock during Dispose");
                return;
            }
            
            try
            {
                if (_isDisposed) return;
                _isDisposed = true;
                
                _httpClient?.Dispose();
                _httpClient = null;
                _semaphore.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during dispose");
            }
            
            GC.SuppressFinalize(this);
        }

        private async Task UpdateDataAsync()
        {
            try
            {
                if (_isDisposed) return;
                
                // Wait for semaphore with timeout to prevent deadlocks
                if (!await _semaphore.WaitAsync(TimeSpan.FromSeconds(5)))
                {
                    _logger?.LogError("Timeout waiting for semaphore lock during UpdateDataAsync");
                    return;
                }
                
                try
                {
                    if (_isDisposed) return;
                    
                    // Check if update is needed
                    if ((DateTime.Now - _lastUpdateTime).TotalMilliseconds < UpdateInterval)
                    {
                        return;
                    }

                    // Check if client is available
                    if (_httpClient == null)
                    {
                        _logger?.LogError("HttpClient is null, attempting to reconnect");
                        if (!Connect())
                        {
                            return;
                        }
                    }

                    // Build the URL
                    string url = Path;
                    if (!string.IsNullOrEmpty(url))
                    {
                        if (!url.StartsWith("/"))
                            url = "/" + url;
                    }

                    // Add query parameters if provided
                    if (!string.IsNullOrEmpty(QueryParams))
                    {
                        string[] pairs = QueryParams.Split(';');
                        url = url + (url.Contains("?") ? "&" : "?") + string.Join("&", pairs);
                    }

                    // Create the request message
                    var requestMessage = new System.Net.Http.HttpRequestMessage
                    {
                        Method = new System.Net.Http.HttpMethod(Method.ToString()),
                        RequestUri = new Uri(url, UriKind.Relative)
                    };

                    // Add headers if provided
                    if (!string.IsNullOrEmpty(Headers))
                    {
                        try
                        {
                            var headers = Headers.Split(';')
                                .Select(pair => pair.Split('='))
                                .Where(parts => parts.Length == 2)
                                .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim());
                            foreach (var header in headers)
                            {
                                requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError($"Error parsing headers: {ex.Message}");
                        }
                    }

                    // Add body content for non-GET requests
                    if (Method != HttpMethod.GET && !string.IsNullOrEmpty(Body))
                    {
                        HttpContent content = null;
                        
                        switch (ContentType)
                        {
                            case ContentType.Form:
                            case ContentType.FormUrlEncoded:
                                try {
                                    var formData = JsonConvert.DeserializeObject<Dictionary<string, string>>(Body);
                                    content = new FormUrlEncodedContent(formData);
                                }
                                catch (Exception ex) {
                                    _logger?.LogError($"Error parsing form data: {ex.Message}");
                                }
                                break;
                            
                            case ContentType.Json:
                                content = new StringContent(Body, Encoding.UTF8, "application/json");
                                break;
                            
                            case ContentType.Xml:
                                content = new StringContent(Body, Encoding.UTF8, "application/xml");
                                break;
                            
                            case ContentType.Raw:
                            default:
                                content = new StringContent(Body);
                                break;
                        }

                        if (content != null)
                        {
                            requestMessage.Content = content;
                        }
                    }

                    // Set timeout
                    var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(Timeout));

                    try
                    {
                        // Execute request
                        HttpResponseMessage response = await _httpClient.SendAsync(requestMessage, cancellationTokenSource.Token);
                        response.EnsureSuccessStatusCode();

                        // Process response
                        string jsonResponse = await response.Content.ReadAsStringAsync();
                        _lastData = JObject.Parse(jsonResponse);
                        _lastUpdateTime = DateTime.Now;
                        _isConnected = true;
                    }
                    catch (HttpRequestException ex)
                    {
                        _isConnected = false;
                        _logger?.LogError($"HTTP request failed: {ex.Message}");
                        _httpClient.Dispose();
                        _httpClient = null;
                        // Reconnect on next attempt - the client itself doesn't need recreation
                    }
                    catch (TaskCanceledException ex)
                    {
                        _isConnected = false;
                        _logger?.LogError($"HTTP request timed out: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        _isConnected = false;
                        _logger?.LogError($"Error in HTTP request: {ex.Message}");
                    }
                }
                finally
                {
                    try { _semaphore.Release(); }
                    catch (ObjectDisposedException) { /* Ignore if already disposed */ }
                }
            }
            catch (Exception ex)
            {
                _isConnected = false;
                _logger?.LogError($"Error updating data: {ex.Message}");
            }
        }

        [Method("读取", description: "读取JSON路径的值")]
        public DriverReturnValueModel Read(DriverAddressIoArgModel ioArg)
        {
            try
            {
                if (_isDisposed)
                {
                    return new DriverReturnValueModel
                    {
                        Value = null,
                        StatusType = VaribaleStatusTypeEnum.Bad
                    };
                }
                
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
                    case DataTypeEnum.Uint16:
                    case DataTypeEnum.Uint32:
                    case DataTypeEnum.Uint64:
                        value = token.Value<ulong>();
                        break;
                    case DataTypeEnum.Float:
                    case DataTypeEnum.Double:
                        value = token.Value<double>();
                        break;
                    case DataTypeEnum.Bool:
                        value = token.Value<bool>();
                        break;
                    case DataTypeEnum.Utf8String:
                        value = token.Value<string>();
                        break;
                    default:
                        value = token.ToString();
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

        #region IAddressDefinitionProvider Implementation
        public Dictionary<string, AddressDefinitionInfo> GetAddressDefinitions()
        {
            return HttpJsonAddressDefinitions.GetDefinitions();
        }
        #endregion
    }
}