using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;
using PluginInterface;
using System.Collections.Concurrent;

namespace Plugin.Drivers.HTTPServer
{
    [DriverSupported("HTTP JSON Server")]
    [DriverInfo("HTTP JSON Server", "1.0.0", "HTTP server for receiving robot action and screw tightening information")]
    public class HttpJsonServerDriver : IDriver
    {
        private HttpListener _listener;
        private Thread _listenerThread;
        private bool _isRunning;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private bool _isDisposed;
        
        // Changed from ConcurrentDictionary to List
        private readonly List<RobotArmPosition> _robotPositions = new List<RobotArmPosition>();
        private readonly List<ScrewTighteningResult> _screwResults = new List<ScrewTighteningResult>();
        
        // Thread safety for the lists
        private readonly object _robotPositionsLock = new object();
        private readonly object _screwResultsLock = new object();
        
        // Required properties from IDriver
        [ConfigParameter("设备ID")]
        public string DeviceId { get; private set; }
        public bool IsConnected { get; private set; }
        [ConfigParameter("超时时间")]
        public int Timeout { get; private set; } = 15000; // Default 15 seconds
        [ConfigParameter("最小周期")]
        public uint MinPeriod { get; private set; } = 1000; // Default 1 second
        public ILogger _logger { get; set; }


        public string ListenIP { get; set; } = "0.0.0.0";
        
        [ConfigParameter("端口号")]
        public int Port { get; set; } = 18080;
        
        [ConfigParameter("机器人动作接口")]
        public string RobotEndpoint { get; set; } = "/sendRobActionInfos";
        

        public HttpJsonServerDriver(string device, ILogger logger)
        {
            DeviceId = device;
            IsConnected = false;
            _logger = logger;
            _isDisposed = false;
        }

        public bool Connect()
        {
            try
            {
                if (!_semaphore.Wait(TimeSpan.FromSeconds(10)))
                {
                    _logger?.LogWarning("Timeout waiting for semaphore in Connect");
                    return false;
                }

                try
                {
                    // 确保关闭之前的监听器
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

                    // 创建新的 HttpListener
                    _listener = new HttpListener();
                    
                    string prefix = $"http://{(ListenIP == "0.0.0.0" ? "*" : ListenIP)}:{Port}/";
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

                    _isRunning = true;
                    _listenerThread = new Thread(HandleRequests);
                    _listenerThread.IsBackground = true;
                    _listenerThread.Start();

                    IsConnected = true;
                    _logger?.LogInformation($"HTTP JSON Server started on {ListenIP}:{Port}");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error in Connect method");
                    
                    try
                    {
                        _isRunning = false;
                        if (_listener != null)
                        {
                            try
                            {
                                if (_listener.IsListening)
                                {
                                    _listener.Stop();
                                }
                                _listener.Close();
                            }
                            catch { /* 忽略清理过程中的错误 */ }
                            _listener = null;
                        }
                    }
                    catch (Exception cleanupEx) 
                    { 
                        _logger?.LogWarning(cleanupEx, "Error during cleanup after failed connection");
                    }
                    
                    IsConnected = false;
                    return false;
                }
                finally
                {
                    try 
                    {
                        _semaphore.Release();
                    }
                    catch (ObjectDisposedException)
                    {
                        _logger?.LogWarning("Semaphore was already disposed during Connect");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Critical error in Connect");
                return false;
            }
        }

        private void HandleRequests()
        {
            while (_isRunning)
            {
                try
                {
                    // GetContext() blocks until a request comes in
                    HttpListenerContext context = _listener.GetContext();
                    
                    // Process each request in a separate thread from the pool
                    ThreadPool.QueueUserWorkItem(_ => 
                    {
                        try
                        {
                            ProcessRequest(context);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "Error processing request");
                            try 
                            {
                                // Ensure we always close the response
                                context.Response.StatusCode = 500;
                                context.Response.Close();
                            }
                            catch { /* Ignore errors in error handling */ }
                        }
                    });
                }
                catch (HttpListenerException)
                {
                    // This can happen when the listener is stopped
                    if (_isRunning)
                    {
                        _logger?.LogError("HTTP Listener exception occurred while running");
                    }
                }
                catch (InvalidOperationException)
                {
                    // This can happen when the listener is stopped
                    if (_isRunning)
                    {
                        _logger?.LogError("Invalid operation exception occurred while running");
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Unexpected error in HTTP listener thread");
                    
                    // Add small delay to prevent tight loop if there's a persistent error
                    Thread.Sleep(1000);
                }
            }
        }

        // Close method for IDriver
        public bool Close()
        {
            try
            {
                if (_isDisposed) return false;

                if (!_semaphore.Wait(TimeSpan.FromSeconds(10)))
                {
                    _logger?.LogWarning("Timeout waiting for semaphore in Close");
                    return false;
                }

                try
                {
                    if (_isDisposed) return false;

                    // 停止运行标志
                    _isRunning = false;
                    
                    // 停止 HTTP 监听器
                    if (_listener != null)
                    {
                        try 
                        {
                            _listener.Stop();
                            // 注意：不在此处调用 Close() 和设置为 null，留给 Dispose 处理
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "Error stopping HTTP listener");
                        }
                    }

                    // 等待线程结束（添加超时防止无限等待）
                    if (_listenerThread != null && _listenerThread.IsAlive)
                    {
                        if (!_listenerThread.Join(TimeSpan.FromSeconds(5)))
                        {
                            _logger?.LogWarning("Listener thread did not exit within timeout");
                        }
                        // 注意：不在此处设置为 null，留给 Dispose 处理
                    }
                    
                    IsConnected = false;
                    return true;
                }
                finally
                {
                    try { _semaphore.Release(); }
                    catch (ObjectDisposedException) { /* 已处理 */ }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error disconnecting from HTTP JSON Server");
                return false;
            }
        }

        // Modified Read method to process address strings
        [Method("读取数据", description: "根据地址格式读取机器人位置")]
        public DriverReturnValueModel Read(DriverAddressIoArgModel ioArg)
        {
            try
            {
                if (_isDisposed) return new DriverReturnValueModel { StatusType = VaribaleStatusTypeEnum.Bad };

                if (!_semaphore.Wait(TimeSpan.FromSeconds(10)))
                {
                    return new DriverReturnValueModel { StatusType = VaribaleStatusTypeEnum.Bad };
                }

                try
                {
                    if (_isDisposed) return new DriverReturnValueModel { StatusType = VaribaleStatusTypeEnum.Bad };

                    string address = ioArg.Address;
                    
                    // Parse address to determine what data to return
                    // Format: type,id,property[,index]
                    // Example: robot,RB01,position,0 or screw,S001,result
                    
                    string[] parts = address.Split(',');
                    if (parts.Length < 3)
                    {
                        return new DriverReturnValueModel 
                        { 
                            StatusType = VaribaleStatusTypeEnum.Bad,
                            Message = "Invalid address format. Use: type,id,property[,index]"
                        };
                    }

                    string type = parts[0].Trim().ToLower();
                    string id = parts[1].Trim();
                    string property = parts[2].Trim();
                    
                    // Since we're now using Lists, we need to find the item by its ID in the list
                    JObject jsonObj = null;
                    string jsonPath = property;
                    
                    // Handle array indexing for positions
                    if (parts.Length >= 4 && int.TryParse(parts[3], out int index))
                    {
                        jsonPath = $"{property}[{index}]";
                    }

                    if (type == "robot")
                    {
                        RobotArmPosition position = null;
                        
                        lock (_robotPositionsLock)
                        {
                            position = _robotPositions.Find(p => p.RobCode == id);
                        }
                        
                        if (position == null)
                        {
                            return new DriverReturnValueModel 
                            { 
                                StatusType = VaribaleStatusTypeEnum.Bad,
                                Message = $"Robot with ID '{id}' not found" 
                            };
                        }
                        
                        string json = JsonConvert.SerializeObject(position);
                        jsonObj = JObject.Parse(json);
                    }
                    else if (type == "screw")
                    {
                        ScrewTighteningResult result = null;
                        int screwId = int.Parse(id);
                        
                        lock (_screwResultsLock)
                        {
                            result = _screwResults.Find(r => r.ScrewIndex == screwId);
                        }
                        
                        if (result == null)
                        {
                            return new DriverReturnValueModel 
                            { 
                                StatusType = VaribaleStatusTypeEnum.Bad,
                                Message = $"Screw result with ID '{id}' not found" 
                            };
                        }
                        
                        string json = JsonConvert.SerializeObject(result);
                        jsonObj = JObject.Parse(json);
                    }
                    else
                    {
                        return new DriverReturnValueModel 
                        { 
                            StatusType = VaribaleStatusTypeEnum.Bad,
                            Message = $"Unknown data type: {type}. Use 'robot' or 'screw'" 
                        };
                    }
                    
                    // Extract value using JSON path
                    JToken token = jsonObj.SelectToken(jsonPath);
                    if (token == null)
                    {
                        return new DriverReturnValueModel 
                        { 
                            StatusType = VaribaleStatusTypeEnum.Bad,
                            Message = $"Property '{jsonPath}' not found" 
                        };
                    }
                    
                    // Convert token to appropriate .NET type
                    object value = token.Type switch
                    {
                        JTokenType.String => token.Value<string>(),
                        JTokenType.Integer => token.Value<int>(),
                        JTokenType.Float => token.Value<double>(),
                        JTokenType.Boolean => token.Value<bool>(),
                        JTokenType.Date => token.Value<DateTime>(),
                        JTokenType.Array => token.Values<object>().ToArray(),
                        JTokenType.Object => token.ToString(),
                        _ => token.ToString()
                    };
                    
                    return new DriverReturnValueModel 
                    { 
                        Value = value, 
                        StatusType = VaribaleStatusTypeEnum.Good 
                    };
                }
                finally
                {
                    try
                    {
                        _semaphore.Release();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Semaphore may have been disposed
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Error reading address {ioArg.Address}");
                return new DriverReturnValueModel 
                { 
                    StatusType = VaribaleStatusTypeEnum.Bad,
                    Message = ex.Message
                };
            }
        }

        // Modified to return all robot positions and clear the list
        [Method("读取机器人位置", description: "读取机器人位置")]
        public DriverReturnValueModel ReadRobotPosition(DriverAddressIoArgModel ioArg)
        {
            try
            {
                if (_isDisposed) return new DriverReturnValueModel { StatusType = VaribaleStatusTypeEnum.Bad };

                _logger?.LogInformation("ReadRobotPosition called");
                
                List<RobotArmPosition> positions;
                lock (_robotPositionsLock)
                {
                    _logger?.LogInformation($"Current robot positions count: {_robotPositions.Count}");
                    
                    if (_robotPositions.Count == 0) 
                    {
                        _logger?.LogInformation("No robot positions available to read");
                        return new DriverReturnValueModel { 
                            StatusType = VaribaleStatusTypeEnum.Good, 
                            Value = "[]" 
                        };
                    }
                    
                    // Make a copy of all positions
                    positions = new List<RobotArmPosition>(_robotPositions);
                    
                    // Clear the original list after reading
                    _robotPositions.Clear();
                    
                    _logger?.LogInformation($"Copied {positions.Count} robot positions and cleared original list");
                }

                // Convert list to JSON
                string json = JsonConvert.SerializeObject(positions);
                _logger?.LogInformation($"Serialized {positions.Count} robot positions to JSON");
                
                return new DriverReturnValueModel
                {
                    Value = json,
                    StatusType = VaribaleStatusTypeEnum.Good
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Error reading robot positions for address {ioArg.Address}");
                return new DriverReturnValueModel
                {
                    StatusType = VaribaleStatusTypeEnum.Bad,
                    Message = ex.Message
                };
            }
        }

        // Modified to return all screw results and clear the list
        [Method("读取螺丝结果", description: "读取螺丝结果")]
        public DriverReturnValueModel ReadScrewResult(DriverAddressIoArgModel ioArg)
        {
            try
            {
                if (_isDisposed) return new DriverReturnValueModel { StatusType = VaribaleStatusTypeEnum.Bad };

                _logger?.LogInformation("ReadScrewResult called");
                
                List<ScrewTighteningResult> results;
                lock (_screwResultsLock)
                {
                    _logger?.LogInformation($"Current screw results count: {_screwResults.Count}");
                    
                    if (_screwResults.Count == 0) 
                    {
                        _logger?.LogInformation("No screw results available to read");
                        return new DriverReturnValueModel { 
                            StatusType = VaribaleStatusTypeEnum.Good, 
                            Value = "[]" 
                        };
                    }
                    
                    // Make a copy of all results
                    results = new List<ScrewTighteningResult>(_screwResults);
                    
                    // Clear the original list after reading
                    _screwResults.Clear();
                    
                    _logger?.LogInformation($"Copied {results.Count} screw results and cleared original list");
                }

                // Convert list to JSON
                string json = JsonConvert.SerializeObject(results);
                _logger?.LogInformation($"Serialized {results.Count} screw results to JSON");
                
                return new DriverReturnValueModel
                {
                    Value = json,
                    StatusType = VaribaleStatusTypeEnum.Good
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Error reading screw results for address {ioArg.Address}");
                return new DriverReturnValueModel
                {
                    StatusType = VaribaleStatusTypeEnum.Bad,
                    Message = ex.Message
                };
            }
        }

        // Implement WriteAsync for IDriver
        public Task<RpcResponse> WriteAsync(string RequestId, string Method, DriverAddressIoArgModel ioArg)
        {
            // This driver is read-only, so writing is not supported
            return Task.FromResult(new RpcResponse 
            { 
                IsSuccess = false,
                Description = "This driver is read-only and does not support writing"
            });
        }

        private void ProcessRequest(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;

                // Set CORS headers
                response.Headers.Add("Access-Control-Allow-Origin", "*");
                response.Headers.Add("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
                response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

                // Handle preflight OPTIONS request
                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = 200;
                    response.Close();
                    return;
                }

                if (request.HttpMethod == "POST")
                {
                    using var reader = new System.IO.StreamReader(request.InputStream, request.ContentEncoding);
                    string requestBody = reader.ReadToEnd();
                    try {
                        // 验证JSON格式
                        var jsonObj = JsonConvert.DeserializeObject<dynamic>(requestBody);

                        // Process based on endpoint
                        if (request.Url.AbsolutePath.EndsWith(RobotEndpoint))
                        {
                            _logger?.LogInformation($"Received request to {RobotEndpoint} with body: {requestBody.Substring(0, Math.Min(200, requestBody.Length))}...");
                            
                            // 尝试从data对象中获取数据
                            JObject parsedObject;
                            if (requestBody.Contains("\"data\":"))
                            {
                                var wrapper = JObject.Parse(requestBody);
                                if (wrapper["data"] != null)
                                {
                                    parsedObject = wrapper["data"] as JObject;
                                    _logger?.LogInformation("Found data object in JSON");
                                }
                                else
                                {
                                    parsedObject = JObject.Parse(requestBody);
                                }
                            }
                            else
                            {
                                parsedObject = JObject.Parse(requestBody);
                            }
                            
                            if (parsedObject != null)
                            {
                                if (parsedObject.ContainsKey("endEffectorStatus")) 
                                {
                                    _logger?.LogInformation("Processing as robot action");
                                    HandleRobotAction(parsedObject.ToString());
                                }
                                else if (parsedObject.ContainsKey("torque")) 
                                {
                                    _logger?.LogInformation("Processing as screw result");
                                    HandleScrewResult(parsedObject.ToString());
                                }
                                else if (parsedObject.ContainsKey("screwIndex") && parsedObject.ContainsKey("result")) 
                                {
                                    _logger?.LogInformation("Processing as screw result (alternative format)");
                                    HandleScrewResult(parsedObject.ToString());
                                }
                                else 
                                {
                                    _logger?.LogWarning($"Received data doesn't match expected format: {requestBody.Substring(0, Math.Min(200, requestBody.Length))}...");
                                }
                            }
                            else
                            {
                                _logger?.LogWarning("Could not parse JSON data");
                            }
                        }
                        else
                        {
                            response.StatusCode = 404;
                            byte[] errorBuffer = Encoding.UTF8.GetBytes("{\"error\":\"Endpoint not found\"}");
                            response.ContentLength64 = errorBuffer.Length;
                            response.OutputStream.Write(errorBuffer, 0, errorBuffer.Length);
                            response.Close();
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error processing HTTP request");
                        try
                        {
                            context.Response.StatusCode = 500;
                            byte[] errorBuffer = Encoding.UTF8.GetBytes("{\"error\":\"Internal server error\"}");
                            context.Response.ContentLength64 = errorBuffer.Length;
                            context.Response.OutputStream.Write(errorBuffer, 0, errorBuffer.Length);
                        }
                        catch
                        {
                            // Ignore errors in error handling
                        }
                        return;
                    }
                    

                    // Send success response
                    byte[] buffer = Encoding.UTF8.GetBytes("{\"result\":\"success\"}");
                    response.ContentLength64 = buffer.Length;
                    response.ContentType = "application/json";
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                }
                else
                {
                    response.StatusCode = 405; // Method Not Allowed
                    byte[] errorBuffer = Encoding.UTF8.GetBytes("{\"error\":\"Method not allowed\"}");
                    response.ContentLength64 = errorBuffer.Length;
                    response.OutputStream.Write(errorBuffer, 0, errorBuffer.Length);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing HTTP request");
                try
                {
                    context.Response.StatusCode = 500;
                    byte[] errorBuffer = Encoding.UTF8.GetBytes("{\"error\":\"Internal server error\"}");
                    context.Response.ContentLength64 = errorBuffer.Length;
                    context.Response.OutputStream.Write(errorBuffer, 0, errorBuffer.Length);
                }
                catch
                {
                    _logger?.LogError(ex, "Error processing HTTP request");
                }
            }
            finally
            {
                context.Response.Close();
            }
        }

        // Modified to add to list instead of dictionary
        private void HandleRobotAction(string jsonData)
        {
            try
            {
                _logger?.LogInformation($"Parsing robot action JSON: {jsonData.Substring(0, Math.Min(200, jsonData.Length))}...");
                
                // 尝试直接从JSON中提取必要字段
                JObject json = JObject.Parse(jsonData);
                
                var robotPosition = new RobotArmPosition
                {
                    RobCode = json["robCode"]?.ToString(),
                    DeviceType = json["deviceType"]?.ToObject<int>() ?? 0,
                    EndEffectorCode = json["endEffectorCode"]?.ToString(),
                    EndEffectorStatus = json["endEffectorStatus"]?.ToObject<int>() ?? 0,
                    Timestamp = DateTime.Now
                };
                
                // 处理位置数组
                if (json["positions"] != null && json["positions"].Type == JTokenType.Array)
                {
                    var posArray = json["positions"].ToObject<float[]>();
                    if (posArray != null && posArray.Length > 0)
                    {
                        robotPosition.Positions = posArray;
                    }
                }
                
                if (!string.IsNullOrEmpty(robotPosition.RobCode))
                {
                    // Add to list with thread safety
                    lock (_robotPositionsLock)
                    {
                        _robotPositions.Add(robotPosition);
                        _logger?.LogInformation($"Added robot position to list. Current count: {_robotPositions.Count}");
                    }
                    
                    _logger?.LogInformation($"Received position update for robot {robotPosition.RobCode}, endEffector: {robotPosition.EndEffectorCode}");
                }
                else
                {
                    _logger?.LogWarning($"Invalid robot data, missing robCode: {jsonData.Substring(0, Math.Min(200, jsonData.Length))}...");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Error processing robot action: {jsonData}");
            }
        }

        // Modified to add to list instead of dictionary
        private void HandleScrewResult(string jsonData)
        {
            try
            {
                _logger?.LogInformation($"Parsing screw result JSON: {jsonData.Substring(0, Math.Min(200, jsonData.Length))}...");
                
                // 尝试直接从JSON中提取必要字段
                JObject json = JObject.Parse(jsonData);
                
                var screwResult = new ScrewTighteningResult
                {
                    RobCode = json["robCode"]?.ToString(),
                    DeviceType = json["deviceType"]?.ToObject<int>() ?? 0,
                    ScrewIndex = json["screwIndex"]?.ToObject<int>() ?? -1,
                    Torque = json["torque"]?.ToObject<float>() ?? 0f,
                    Result = json["result"]?.ToObject<bool>() ?? false,
                    Timestamp = DateTime.Now
                };
                
                if (screwResult.ScrewIndex >= 0)
                {
                    // Add to list with thread safety
                    lock (_screwResultsLock)
                    {
                        _screwResults.Add(screwResult);
                        _logger?.LogInformation($"Added screw result to list. Current count: {_screwResults.Count}");
                    }
                    
                    _logger?.LogInformation($"Received screw tightening result for {screwResult.ScrewIndex}, result: {screwResult.Result}");
                }
                else
                {
                    _logger?.LogWarning($"Invalid screw data, missing or invalid screwIndex: {jsonData.Substring(0, Math.Min(200, jsonData.Length))}...");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Error processing screw result: {jsonData}");
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing && !_isDisposed)
            {
                _isDisposed = true;
                
                try
                {
                    // 确保停止运行
                    _isRunning = false;
                    
                    // 清理 HTTP 监听器资源
                    if (_listener != null)
                    {
                        try
                        {
                            // 如果连接仍然活跃，先尝试停止
                            if (IsConnected)
                            {
                                _listener.Stop();
                            }
                            
                            _listener.Close();
                            _listener = null;
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "Error closing HTTP listener during disposal");
                        }
                    }
                    
                    // 清理线程资源
                    _listenerThread = null;
                    
                    // 添加短暂延迟以确保端口释放
                    Thread.Sleep(500);
                    
                    // 确保连接状态为关闭
                    IsConnected = false;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error disposing HTTP JSON Server driver");
                }
            }
        }
    }

    // Data models
    public class RobotActionInfo
    {
        public string robCode { get; set; }
        public int deviceType { get; set; }
        public float[] position { get; set; }
        public string endEffectorCode { get; set; }
    }

    public class RobotArmPosition
    {
        public string RobCode { get; set; }
        public int DeviceType { get; set; }
        public float[] Positions { get; set; } = new float[6];
        public string EndEffectorCode { get; set; }
        public int EndEffectorStatus { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class ScrewTighteningResult
    {
        public string RobCode { get; set; }
        public int DeviceType { get; set; }
        public int ScrewIndex { get; set; }
        public float Torque { get; set; }
        public bool Result { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}