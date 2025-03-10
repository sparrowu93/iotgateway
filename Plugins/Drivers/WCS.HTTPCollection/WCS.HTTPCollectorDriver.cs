using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PluginInterface;
using Robot.DataCollector.Models;
using System.IO;

namespace Robot.DataCollector
{
    [DriverSupported("Robot.DataCollector")]
    [DriverInfo("Robot.DataCollector", "1.0.0", "HTTP JSON Robot Data Collector 自动打磨 AVG")]
    public class RobotDataCollectorDriver : IDriver, IAddressDefinitionProvider
    {
        #region Private Fields
        
        // Client-related
        private HttpClient _httpClient;
        private bool _isConnected;
        private JObject _lastRobotState;
        private DateTime _lastStateUpdateTime;
        private TaskNotifyConfig _lastTaskConfig;
        private DateTime _lastConfigUpdateTime;
        private List<Models.TaskStatus> _taskNotifications = new List<Models.TaskStatus>();
        private readonly object _taskNotificationsLock = new object();
        
        // Server-related
        private HttpListener _listener;
        private Thread _listenerThread;
        private bool _isRunning;
        
        // Thread safety
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private bool _isDisposed;
        private readonly object _lock = new object();
        
        // 状态更新任务
        private CancellationTokenSource _robotStateTokenSource;
        private Task _robotStateUpdateTask;
        
        #endregion
        
        #region Configuration Parameters
        
        // Common parameters
        [ConfigParameter("设备ID")]
        public string DeviceId { get; private set; }
        
        public bool IsConnected => _isConnected;
        
        [ConfigParameter("超时时间(毫秒)")]
        public int Timeout { get; set; } = 15000;
        
        [ConfigParameter("最小周期(毫秒)")]
        public uint MinPeriod { get; set; } = 5000;
        
        public ILogger _logger { get; set; }
        
        // Client parameters
        [ConfigParameter("机器人状态API地址")]
        public string RobotStateUrl { get; set; } = "http://localhost:8080/wcs/robots/state";
        
        [ConfigParameter("任务配置API地址")]
        public string TaskConfigUrl { get; set; } = "http://localhost:8080/wcs/system/config/tasknotify";
        
        [ConfigParameter("状态更新间隔(毫秒)")]
        public int StateUpdateInterval { get; set; } = 5000;
        
        [ConfigParameter("配置更新间隔(毫秒)")]
        public int ConfigUpdateInterval { get; set; } = 30000;
        
        [ConfigParameter("当前服务地址")]
        public string CurrentServiceUrl { get; set; } = "http://localhost:10000/test";
        
        // Server parameters
        [ConfigParameter("监听IP")]
        public string ListenIP { get; set; } = "0.0.0.0";
        
        [ConfigParameter("端口号")]
        public int Port { get; set; } = 10000;
        
        [ConfigParameter("任务通知接口")]
        public string TaskNotifyEndpoint { get; set; } = "/test";
        
        #endregion
        
        public RobotDataCollectorDriver(string device, ILogger logger)
        {
            DeviceId = device;
            _isConnected = false;
            _lastRobotState = null;
            _lastStateUpdateTime = DateTime.MinValue;
            _lastTaskConfig = null;
            _lastConfigUpdateTime = DateTime.MinValue;
            _logger = logger;
            _isDisposed = false;
        }
        
        #region IDriver Implementation
        
        public bool Connect()
        {
            try
            {
                if (_isDisposed) return false;
                
                // Use a timeout for semaphore to prevent deadlocks
                if (!_semaphore.Wait(TimeSpan.FromSeconds(10)))
                {
                    _logger?.LogError("Timeout waiting for semaphore lock during Connect");
                    return false;
                }
                
                try
                {
                    if (_isDisposed) return false;
                    
                    // Initialize client
                    // Dispose old client and create a new one
                    if (_httpClient != null)
                    {
                        _httpClient.Dispose();
                    }
                    
                    // Create HTTP client
                    var handler = new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
                    };
                    
                    _httpClient = new HttpClient(handler);
                    _httpClient.Timeout = TimeSpan.FromMilliseconds(Timeout);
                    
                    // Initialize server
                    // Cleanup previous listener
                    if (_listener != null)
                    {
                        try
                        {
                            if (_listener.IsListening)
                            {
                                _listener.Stop();
                            }
                            
                            _listener.Close();
                            _listener = null;
                            
                            Thread.Sleep(1000);
                            
                            _logger?.LogInformation("Successfully cleaned up previous listener");
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "Error cleaning up previous listener");
                            _listener = null;
                        }
                    }
                    
                    // Start new HTTP listener
                    _listener = new HttpListener();
                    
                    string prefix = $"http://{(ListenIP == "0.0.0.0" ? "*" : ListenIP)}:{Port}{TaskNotifyEndpoint}/";
                    _logger?.LogInformation($"Adding prefix: {prefix}");
                    _listener.Prefixes.Add(prefix);
                    
                    try
                    {
                        _listener.Start();
                        _logger?.LogInformation("Listener started successfully");
                    }
                    catch (HttpListenerException hlex)
                    {
                        _logger?.LogError(hlex, $"Failed to start listener. Error code: {hlex.ErrorCode}");
                        
                        if (hlex.ErrorCode == 32 || hlex.ErrorCode == 183)
                        {
                            _logger?.LogWarning($"Port {Port} may already be in use. Waiting for it to be released...");
                            Thread.Sleep(3000);
                            
                            try 
                            {
                                _listener.Start();
                                _logger?.LogInformation("Listener started successfully on retry");
                            }
                            catch (Exception retryEx)
                            {
                                _logger?.LogError(retryEx, "Failed to start listener on retry");
                                throw;
                            }
                        }
                        else
                        {
                            throw;
                        }
                    }
                    
                    // Start listener thread
                    _isRunning = true;
                    _listenerThread = new Thread(HandleRequests);
                    _listenerThread.IsBackground = true;
                    _listenerThread.Start();
                    
                    _isConnected = true;
                    _logger?.LogInformation($"Robot Data Collector initialized: Client to {RobotStateUrl}, Server on {ListenIP}:{Port}{TaskNotifyEndpoint}");
                    
                    // Initial data fetch
                    Task.Run(async () => {
                        await UpdateTaskConfigAsync();
                    }).Wait();

                    // 定时获取机器人状态 频率 UpdateStateInterval
                    // 先执行一次获取初始状态
                    Task.Run(async () => {
                        await UpdateRobotStateAsync();
                    }).Wait();
                    
                    // 启动周期性任务
                    _robotStateTokenSource = new CancellationTokenSource();
                    _robotStateUpdateTask = RunPeriodicRobotStateUpdateAsync(_robotStateTokenSource.Token);

                    
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
                _logger?.LogError(ex, "Failed to connect to robot data endpoints");
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
                    
                    // Stop HTTP listener
                    _isRunning = false;
                    if (_listener != null && _listener.IsListening)
                    {
                        _listener.Stop();
                    }
                    
                    // Wait for listener thread to exit
                    if (_listenerThread != null && _listenerThread.IsAlive)
                    {
                        _listenerThread.Join(TimeSpan.FromSeconds(5));
                    }
                    
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
                
                // Stop HTTP listener
                _isRunning = false;
                try
                {
                    if (_listener != null)
                    {
                        if (_listener.IsListening)
                        {
                            _listener.Stop();
                        }
                        _listener.Close();
                        _listener = null;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error disposing HTTP listener");
                }
                
                // 停止定期更新机器人状态的任务
                if (_robotStateTokenSource != null && !_robotStateTokenSource.IsCancellationRequested)
                {
                    _robotStateTokenSource.Cancel();
                    try
                    {
                        _robotStateUpdateTask?.Wait(1000); // 等待任务完成，最多等待1秒
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error waiting for robot state update task to complete");
                    }
                    _robotStateTokenSource.Dispose();
                }
                
                // Dispose HTTP client
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
                
                // Parse address to determine which data to access
                string[] addressParts = ioArg.Address.Split(':', 2);
                string dataSource = addressParts.Length > 1 ? addressParts[0].Trim().ToLowerInvariant() : "robot";
                string jsonPath = addressParts.Length > 1 ? addressParts[1].Trim() : ioArg.Address.Trim();
                
                // Use specialized methods based on data source
                switch (dataSource)
                {
                    case "robot":
                        ioArg.Address = jsonPath;
                        return ReadRobotState(ioArg);
                        
                    case "task":
                        ioArg.Address = jsonPath;
                        return ReadTaskNotification(ioArg);
                        
                    default:
                        _logger?.LogWarning($"Unknown data source: {dataSource}");
                        return new DriverReturnValueModel
                        {
                            Value = null,
                            StatusType = VaribaleStatusTypeEnum.Bad
                        };
                }
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
        
        [Method("读取机器人状态", description: "读取机器人状态数据")]
        public DriverReturnValueModel ReadRobotState(DriverAddressIoArgModel ioArg)
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
                
                // Access robot state data
                JToken token = null;
                lock (_lock)
                {
                    if (_lastRobotState == null)
                    {
                        _logger?.LogWarning("No robot state data available");
                        return new DriverReturnValueModel
                        {
                            Value = null,
                            StatusType = VaribaleStatusTypeEnum.Good
                        };
                    }
                    
                    token = _lastRobotState.SelectToken(ioArg.Address);
                }
                
                if (token == null)
                {
                    _logger?.LogWarning($"Path '{ioArg.Address}' not found in robot state data");
                    return new DriverReturnValueModel
                    {
                        Value = null,
                        StatusType = VaribaleStatusTypeEnum.Bad
                    };
                }
                
                // Convert value based on the requested type
                return ConvertJTokenToValue(token, ioArg.ValueType);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error reading robot state data");
                return new DriverReturnValueModel
                {
                    Value = null,
                    StatusType = VaribaleStatusTypeEnum.Bad
                };
            }
        }
        
        [Method("读取任务通知", description: "读取最新的任务通知数据")]
        public DriverReturnValueModel ReadTaskNotification(DriverAddressIoArgModel ioArg)
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
                
                // Access task notification data
                JToken token = null;
                lock (_taskNotificationsLock)
                {
                    if (_taskNotifications.Count == 0)
                    {
                        _logger?.LogWarning("No task notifications available");
                        return new DriverReturnValueModel
                        {
                            Value = null,
                            StatusType = VaribaleStatusTypeEnum.Good
                        };
                    }
                    
                    // Get the latest task notification
                    JObject taskJson = JObject.FromObject(_taskNotifications[_taskNotifications.Count - 1]);
                    token = taskJson.SelectToken(ioArg.Address);
                }
                
                if (token == null)
                {
                    _logger?.LogWarning($"Path '{ioArg.Address}' not found in task notification data");
                    return new DriverReturnValueModel
                    {
                        Value = null,
                        StatusType = VaribaleStatusTypeEnum.Good
                    };
                }
                
                // Convert value based on the requested type
                return ConvertJTokenToValue(token, ioArg.ValueType);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error reading task notification data");
                return new DriverReturnValueModel
                {
                    Value = null,
                    StatusType = VaribaleStatusTypeEnum.Bad
                };
            }
        }
        
        // Helper method to convert JToken to the appropriate value type
        private DriverReturnValueModel ConvertJTokenToValue(JToken token, DataTypeEnum valueType)
        {
            try
            {
                // Convert value based on the requested type
                object value;
                switch (valueType)
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
                _logger?.LogError(ex, "Error converting token value: {0}", ex.Message);
                return new DriverReturnValueModel
                {
                    Value = null,
                    StatusType = VaribaleStatusTypeEnum.Bad
                };
            }
        }
        
        public async Task<RpcResponse> WriteAsync(string RequestId, string Method, DriverAddressIoArgModel ioArg)
        {
            return await Task.FromResult(new RpcResponse
            {
                IsSuccess = false,
                Description = "Write operation is not supported in Robot Data Collector driver"
            });
        }
        
        #endregion
        
        #region IAddressDefinitionProvider Implementation
        
        public Dictionary<string, AddressDefinitionInfo> GetAddressDefinitions()
        {
            return RobotAddressDefinitions.GetDefinitions();
        }
        
        #endregion
        
        #region HTTP Client Methods
        
        /// <summary>
        /// 运行周期性的机器人状态更新任务
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>任务</returns>
        private async Task RunPeriodicRobotStateUpdateAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger?.LogInformation($"启动周期性机器人状态更新任务，周期: {StateUpdateInterval} 毫秒");
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(StateUpdateInterval));
                
                while (await timer.WaitForNextTickAsync(cancellationToken))
                {
                    try
                    {
                        await UpdateRobotStateAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "周期更新机器人状态时发生错误");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消，不需特殊处理
                _logger?.LogInformation("机器人状态更新任务已取消");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "周期更新机器人状态任务发生异常");
            }
        }

        private async Task UpdateRobotStateAsync()
        {
            try
            {
                if (_isDisposed || _httpClient == null) return;
                
                // Use a timeout for semaphore to prevent deadlocks
                if (!await _semaphore.WaitAsync(TimeSpan.FromSeconds(5)))
                {
                    _logger?.LogError("Timeout waiting for semaphore lock during UpdateRobotStateAsync");
                    return;
                }
                
                try
                {
                    if (_isDisposed || _httpClient == null) return;
                    
                    // Check if update is needed
                    if ((DateTime.Now - _lastStateUpdateTime).TotalMilliseconds < StateUpdateInterval)
                    {
                        return;
                    }
                    
                    // Create request message
                    var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Get, RobotStateUrl);
                    
                    // Execute request with timeout
                    var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(Timeout));
                    HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationTokenSource.Token);
                    
                    response.EnsureSuccessStatusCode();
                    
                    // Process response
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    var robotState = JObject.Parse(jsonResponse);
                    
                    lock (_lock)
                    {
                        _lastRobotState = robotState;
                        _lastStateUpdateTime = DateTime.Now;
                    }
                    
                    _logger?.LogDebug($"Updated robot state data: {jsonResponse}");
                }
                catch (HttpRequestException ex)
                {
                    _logger?.LogError(ex, "HTTP request failed during robot state update");
                }
                catch (TaskCanceledException ex)
                {
                    _logger?.LogError(ex, "HTTP request timed out during robot state update");
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error updating robot state data");
                }
                finally
                {
                    try { _semaphore.Release(); }
                    catch (ObjectDisposedException) { /* Ignore if already disposed */ }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Critical error in UpdateRobotStateAsync");
            }
        }
        
        private async Task UpdateTaskConfigAsync()
        {
            try
            {
                if (_isDisposed || _httpClient == null) return;
                
                // Use a timeout for semaphore to prevent deadlocks
                if (!await _semaphore.WaitAsync(TimeSpan.FromSeconds(5)))
                {
                    _logger?.LogError("Timeout waiting for semaphore lock during UpdateTaskConfigAsync");
                    return;
                }
                
                try
                {
                    if (_isDisposed || _httpClient == null) return;
                    
                    // Check if update is needed
                    if ((DateTime.Now - _lastConfigUpdateTime).TotalMilliseconds < ConfigUpdateInterval)
                    {
                        return;
                    }
                    
                    // Create request message for GET config
                    var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Get, TaskConfigUrl);
                    
                    // Execute request with timeout
                    var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(Timeout));
                    HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationTokenSource.Token);
                    
                    response.EnsureSuccessStatusCode();
                    
                    // Process response
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    var config = JsonConvert.DeserializeObject<TaskNotifyConfig>(jsonResponse);
                    
                    lock (_lock)
                    {
                        _lastTaskConfig = config;
                        _lastConfigUpdateTime = DateTime.Now;
                    }
                    
                    _logger?.LogDebug($"Updated task config data: {jsonResponse}");
                    
                    // Check if configuration needs update
                    bool needsUpdate = false;
                    
                    // Create a deep copy of the configuration for modification
                    var updatedConfig = JsonConvert.DeserializeObject<TaskNotifyConfig>(jsonResponse);
                    
                    // Check if our URL exists in the sinks
                    bool urlExists = false;
                    string notifyKeyToUpdate = null;
                    
                    foreach (var sink in updatedConfig.Sinks)
                    {
                        if (sink.Value.Type.Equals("Http", StringComparison.OrdinalIgnoreCase))
                        {
                            if (sink.Value.Url.Equals(CurrentServiceUrl, StringComparison.OrdinalIgnoreCase))
                            {
                                urlExists = true;
                                break;
                            }
                            else if (sink.Value.Url.Contains(TaskNotifyEndpoint, StringComparison.OrdinalIgnoreCase))
                            {
                                // Found a notify endpoint that matches our path but not full URL
                                notifyKeyToUpdate = sink.Key;
                            }
                        }
                    }
                    
                    // If URL does not exist and we need to update a sink
                    if (!urlExists && notifyKeyToUpdate != null)
                    {
                        updatedConfig.Sinks[notifyKeyToUpdate].Url = CurrentServiceUrl;
                        needsUpdate = true;
                    }
                    // If URL does not exist and we didn't find a matching sink
                    else if (!urlExists && notifyKeyToUpdate == null)
                    {
                        // Add a new sink
                        int nextSinkIndex = updatedConfig.Sinks.Count;
                        updatedConfig.Sinks.Add($"notify{nextSinkIndex}", new NotifySink 
                        { 
                            Type = "Http", 
                            Timeout = 15, 
                            Url = CurrentServiceUrl 
                        });
                        needsUpdate = true;
                    }
                    
                    if (needsUpdate)
                    {
                        // Update the configuration
                        var updateRequest = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, TaskConfigUrl);
                        var content = new StringContent(JsonConvert.SerializeObject(updatedConfig), Encoding.UTF8, "application/json");
                        updateRequest.Content = content;
                        
                        var updateCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(Timeout));
                        var updateResponse = await _httpClient.SendAsync(updateRequest, updateCts.Token);
                        
                        updateResponse.EnsureSuccessStatusCode();
                        
                        string updateResponseJson = await updateResponse.Content.ReadAsStringAsync();
                        var updateResult = JsonConvert.DeserializeObject<TaskNotifyResponse>(updateResponseJson);
                        
                        if (updateResult.Success)
                        {
                            _logger?.LogInformation("Successfully updated task notification configuration");
                            
                            // Update local config
                            lock (_lock)
                            {
                                _lastTaskConfig = updatedConfig;
                            }
                        }
                        else
                        {
                            _logger?.LogError($"Failed to update task configuration: {updateResult.Data.ErrorInfo}");
                        }
                    }
                }
                catch (HttpRequestException ex)
                {
                    _logger?.LogError(ex, "HTTP request failed during task config update");
                }
                catch (TaskCanceledException ex)
                {
                    _logger?.LogError(ex, "HTTP request timed out during task config update");
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error updating task config data");
                }
                finally
                {
                    try { _semaphore.Release(); }
                    catch (ObjectDisposedException) { /* Ignore if already disposed */ }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Critical error in UpdateTaskConfigAsync");
            }
        }
        
        #endregion
        
        #region HTTP Server Methods
        
        private void HandleRequests()
        {
            while (_isRunning)
            {
                try
                {
                    HttpListenerContext context = _listener.GetContext();
                    
                    // Process the request in a separate thread
                    ThreadPool.QueueUserWorkItem(ProcessRequest, context);
                }
                catch (HttpListenerException ex)
                {
                    if (_isRunning)
                    {
                        _logger?.LogError(ex, "Error getting HTTP context");
                    }
                    // If not running, this is expected during shutdown
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Unexpected error in request handling");
                }
            }
        }
        
        private void ProcessRequest(object state)
        {
            var context = (HttpListenerContext)state;
            
            try
            {
                HttpListenerRequest request = context.Request;
                HttpListenerResponse response = context.Response;
                
                // Only accept POST requests
                if (request.HttpMethod != "POST")
                {
                    response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                    response.Close();
                    return;
                }
                
                // Read request body
                string requestBody;
                using (StreamReader reader = new StreamReader(request.InputStream, request.ContentEncoding))
                {
                    requestBody = reader.ReadToEnd();
                }
                
                if (string.IsNullOrEmpty(requestBody))
                {
                    response.StatusCode = (int)HttpStatusCode.BadRequest;
                    response.Close();
                    return;
                }
                
                // Parse task notification
                try
                {
                    var taskStatus = JsonConvert.DeserializeObject<Models.TaskStatus>(requestBody);
                    
                    // Store notification
                    lock (_taskNotificationsLock)
                    {
                        _taskNotifications.Add(taskStatus);
                        
                        // Limit number of stored notifications (keep last 100)
                        const int maxNotifications = 100;
                        if (_taskNotifications.Count > maxNotifications)
                        {
                            _taskNotifications.RemoveRange(0, _taskNotifications.Count - maxNotifications);
                        }
                    }
                    
                    _logger?.LogInformation($"Received task notification: {requestBody}");
                }
                catch (JsonException ex)
                {
                    _logger?.LogError(ex, $"Failed to parse task notification: {requestBody}");
                    response.StatusCode = (int)HttpStatusCode.BadRequest;
                    response.Close();
                    return;
                }
                
                // Send success response
                response.StatusCode = (int)HttpStatusCode.OK;
                response.ContentType = "application/json";
                
                string responseJson = JsonConvert.SerializeObject(new { Success = true });
                byte[] buffer = Encoding.UTF8.GetBytes(responseJson);
                
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.Close();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing request");
                
                try
                {
                    context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    context.Response.Close();
                }
                catch { /* Ignore errors during error handling */ }
            }
        }
        
        #endregion
    }
}