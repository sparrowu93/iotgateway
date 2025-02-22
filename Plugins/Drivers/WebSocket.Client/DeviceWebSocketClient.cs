using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using PluginInterface;

namespace WebSocket.Client
{
    [DriverSupported("WebSocketClient")]
    [DriverInfo("WebSocket.Client", "1.0.0", "WebSocket客户端驱动")]
    public class DeviceWebSocketClient : IDriver, IAddressDefinitionProvider, IDisposable
    {
        private readonly object _lock = new object();
        private bool _isConnected;
        private ClientWebSocket? _webSocket;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _receiveTask;
        private Task? _heartbeatTask;
        private Task? _reconnectionTask;
        private JToken? _lastReceivedData;
        public ILogger _logger { get; set; }

        public string DeviceId { get; }
        public string ServerUrl { get; set; } = "";
        public int ReconnectInterval { get; set; } = 5000;
        public int HeartbeatInterval { get; set; } = 30000;
        public int ConnectionTimeout { get; set; } = 5000;

        public bool IsConnected
        {
            get
            {
                lock (_lock)
                {
                    return _isConnected && _webSocket?.State == WebSocketState.Open;
                }
            }
        }

        public uint MinPeriod { get; set; } = 1000;
        public int Timeout { get; set; } = 3000;

        [ConfigParameter("服务器地址")]
        public string ServerUrlConfig { get; set; } = "ws://localhost:8080/ws";

        [ConfigParameter("自动重连间隔(ms)")]
        public int ReconnectIntervalConfig { get; set; } = 5000;

        [ConfigParameter("心跳间隔(ms)")]
        public int HeartbeatIntervalConfig { get; set; } = 30000;

        [ConfigParameter("连接超时(ms)")]
        public int ConnectionTimeoutConfig { get; set; } = 5000;

        public DeviceWebSocketClient(string deviceId, ILogger logger)
        {
            DeviceId = deviceId;
            _logger = logger;
        }

        public bool Connect()
        {
            try
            {
                _logger.LogInformation("Attempting to connect to WebSocket server at {Url}", ServerUrl);
                
                lock (_lock)
                {
                    if (IsConnected)
                    {
                        _logger.LogInformation("Already connected to WebSocket server");
                        return true;
                    }

                    // Reset connection state
                    _isConnected = false;
                    if (_webSocket != null)
                    {
                        _webSocket.Dispose();
                        _webSocket = null;
                    }
                    if (_cancellationTokenSource != null)
                    {
                        _cancellationTokenSource.Dispose();
                        _cancellationTokenSource = null;
                    }

                    _cancellationTokenSource = new CancellationTokenSource();
                    _webSocket = new ClientWebSocket();
                }
                
                _logger.LogDebug("Starting connection attempt...");
                var connectTask = _webSocket.ConnectAsync(new Uri(ServerUrl), _cancellationTokenSource.Token);
                var timeoutTask = Task.Delay(ConnectionTimeout);
                
                _logger.LogDebug("Waiting for connection or timeout...");
                var completedTask = Task.WhenAny(connectTask, timeoutTask).Result;
                
                if (completedTask == timeoutTask)
                {
                    _logger.LogError("Connection attempt timed out after {Timeout}ms", ConnectionTimeout);
                    HandleDisconnection().Wait();
                    return false;
                }

                if (connectTask.IsFaulted)
                {
                    _logger.LogError(connectTask.Exception, "Connection attempt failed");
                    HandleDisconnection().Wait();
                    return false;
                }

                // Wait for the connection task to complete
                _logger.LogDebug("Connection task completed, waiting for final status");
                connectTask.Wait();

                // Update connection state only after successful connection
                lock (_lock)
                {
                    var state = _webSocket?.State;
                    _logger.LogDebug("WebSocket state after connection: {State}", state);

                    if (state == WebSocketState.Open)
                    {
                        _isConnected = true;
                        _logger.LogInformation("Successfully connected to WebSocket server");
                        StartReceiving();
                        if (HeartbeatInterval > 0)
                        {
                            StartHeartbeat();
                        }
                        return true;
                    }
                    else
                    {
                        _logger.LogError("WebSocket connected but not in Open state: {State}", state);
                        HandleDisconnection().Wait();
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error connecting to WebSocket server");
                HandleDisconnection().Wait();
                return false;
            }
        }

        public bool Close()
        {
            try
            {
                _logger.LogInformation("Closing WebSocket connection");
                
                ClientWebSocket? webSocketToDispose = null;
                CancellationTokenSource? tokenSourceToDispose = null;

                lock (_lock)
                {
                    _isConnected = false;
                    webSocketToDispose = _webSocket;
                    _webSocket = null;
                    tokenSourceToDispose = _cancellationTokenSource;
                    _cancellationTokenSource = null;
                }

                // Cancel and dispose token source
                if (tokenSourceToDispose != null)
                {
                    try
                    {
                        tokenSourceToDispose.Cancel();
                        tokenSourceToDispose.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Error disposing CancellationTokenSource");
                    }
                }

                // Wait for receive task to complete
                if (_receiveTask != null)
                {
                    try
                    {
                        _receiveTask.Wait(TimeSpan.FromSeconds(1));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Error waiting for receive task");
                    }
                }

                // Close and dispose WebSocket
                if (webSocketToDispose != null)
                {
                    try
                    {
                        if (webSocketToDispose.State == WebSocketState.Open)
                        {
                            _logger.LogDebug("Closing WebSocket connection");
                            webSocketToDispose.CloseAsync(
                                WebSocketCloseStatus.NormalClosure,
                                "Client closing",
                                CancellationToken.None).Wait(TimeSpan.FromSeconds(1));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Error closing WebSocket");
                    }
                    finally
                    {
                        webSocketToDispose.Dispose();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Close operation");
                return false;
            }
        }

        private void StartReconnectionTask()
        {
            if (_reconnectionTask != null && !_reconnectionTask.IsCompleted)
            {
                _logger.LogDebug("Reconnection task already running");
                return;
            }

            _logger.LogInformation("Starting reconnection task");
            
            lock (_lock)
            {
                if (_cancellationTokenSource == null)
                {
                    _cancellationTokenSource = new CancellationTokenSource();
                }
            }

            _reconnectionTask = Task.Run(async () =>
            {
                while (!(_cancellationTokenSource?.IsCancellationRequested ?? true))
                {
                    _logger.LogDebug("Checking connection state before reconnection attempt: {Connected}", IsConnected);
                    if (IsConnected)
                    {
                        _logger.LogDebug("Already connected, stopping reconnection task");
                        break;
                    }

                    _logger.LogInformation("Attempting to reconnect...");
                    try
                    {
                        // Create new WebSocket instance for each attempt
                        lock (_lock)
                        {
                            if (_webSocket != null)
                            {
                                _webSocket.Dispose();
                                _webSocket = null;
                            }
                            _webSocket = new ClientWebSocket();
                        }

                        // Attempt to connect
                        await _webSocket.ConnectAsync(new Uri(ServerUrl), _cancellationTokenSource.Token);
                        
                        _logger.LogDebug("Connection attempt completed, checking state: {State}", _webSocket.State);
                        if (_webSocket.State == WebSocketState.Open)
                        {
                            lock (_lock)
                            {
                                _isConnected = true;
                            }
                            _logger.LogInformation("Reconnection successful");
                            StartReceiving();
                            if (HeartbeatInterval > 0)
                            {
                                StartHeartbeat();
                            }
                            break;
                        }
                        else
                        {
                            _logger.LogWarning("Connection attempt completed but WebSocket not in Open state: {State}", _webSocket.State);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error during reconnection attempt");
                    }

                    _logger.LogDebug("Waiting {Interval}ms before next reconnection attempt", ReconnectInterval);
                    await Task.Delay(ReconnectInterval);
                }
            });
        }

        private void StartReceiving()
        {
            _receiveTask = Task.Run(async () =>
            {
                var buffer = new byte[4096];
                try
                {
                    while (!_cancellationTokenSource!.Token.IsCancellationRequested)
                    {
                        // Check WebSocket state before receiving
                        if (_webSocket?.State != WebSocketState.Open)
                        {
                            _logger.LogInformation("WebSocket not in Open state: {State}, handling disconnection", _webSocket?.State);
                            await HandleDisconnection();
                            break;
                        }

                        var result = await _webSocket.ReceiveAsync(
                            new ArraySegment<byte>(buffer),
                            _cancellationTokenSource.Token);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            _logger.LogInformation("Received close message from server");
                            await HandleDisconnection();
                            break;
                        }

                        if (result.MessageType == WebSocketMessageType.Text)
                        {
                            try
                            {
                                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                                _lastReceivedData = JToken.Parse(message);
                                _logger.LogDebug("Received message: {Message}", message);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error parsing received data");
                            }
                        }
                    }
                }
                catch (Exception ex) when (!_cancellationTokenSource!.Token.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Error in receive loop");
                    await HandleDisconnection();
                }
            });
        }

        private void StartHeartbeat()
        {
            _heartbeatTask = Task.Run(async () =>
            {
                try
                {
                    while (_webSocket?.State == WebSocketState.Open && !_cancellationTokenSource!.Token.IsCancellationRequested)
                    {
                        await SendHeartbeat();
                        await Task.Delay(HeartbeatInterval, _cancellationTokenSource.Token);
                    }
                }
                catch (Exception ex) when (!_cancellationTokenSource!.Token.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Error in heartbeat loop");
                    await HandleDisconnection();
                }
            });
        }

        private async Task SendHeartbeat()
        {
            try
            {
                var heartbeat = Encoding.UTF8.GetBytes("{\"type\":\"heartbeat\"}");
                if (_webSocket?.State == WebSocketState.Open)
                {
                    await _webSocket.SendAsync(
                        new ArraySegment<byte>(heartbeat),
                        WebSocketMessageType.Text,
                        true,
                        _cancellationTokenSource!.Token);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending heartbeat");
                throw;
            }
        }

        private async Task HandleDisconnection()
        {
            _logger.LogInformation("Handling disconnection...");
            
            ClientWebSocket? webSocketToDispose = null;
            CancellationTokenSource? tokenSourceToDispose = null;

            lock (_lock)
            {
                _isConnected = false;
                webSocketToDispose = _webSocket;
                _webSocket = null;
                tokenSourceToDispose = _cancellationTokenSource;
                _cancellationTokenSource = null;
            }

            // Clean up WebSocket outside the lock
            if (webSocketToDispose != null)
            {
                try
                {
                    if (webSocketToDispose.State == WebSocketState.Open)
                    {
                        _logger.LogDebug("Closing WebSocket connection");
                        await webSocketToDispose.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Client disconnecting",
                            CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error closing WebSocket connection");
                }
                finally
                {
                    webSocketToDispose.Dispose();
                }
            }

            // Clean up CancellationTokenSource outside the lock
            if (tokenSourceToDispose != null)
            {
                try
                {
                    tokenSourceToDispose.Cancel();
                    tokenSourceToDispose.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error disposing CancellationTokenSource");
                }
            }

            // Start reconnection if needed
            if (ReconnectInterval > 0)
            {
                _logger.LogInformation("Starting reconnection task");
                // Create new CancellationTokenSource for reconnection
                lock (_lock)
                {
                    if (_cancellationTokenSource == null)
                    {
                        _cancellationTokenSource = new CancellationTokenSource();
                    }
                }
                StartReconnectionTask();
            }
        }

        public DriverReturnValueModel Read(DriverAddressIoArgModel ioArg)
        {
            var returnValue = new DriverReturnValueModel();
            
            try
            {
                if (_lastReceivedData == null)
                {
                    _logger.LogWarning("No data received yet");
                    returnValue.StatusType = VaribaleStatusTypeEnum.Bad;
                    returnValue.Message = "No data received yet";
                    return returnValue;
                }

                var value = GetJsonValue(_lastReceivedData, ioArg.Address);
                if (value != null)
                {
                    returnValue.Value = value.ToString();
                    returnValue.StatusType = VaribaleStatusTypeEnum.Good;
                }
                else
                {
                    returnValue.StatusType = VaribaleStatusTypeEnum.Bad;
                    returnValue.Message = "Value not found at specified address";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading value for address {Address}", ioArg.Address);
                returnValue.StatusType = VaribaleStatusTypeEnum.Bad;
                returnValue.Message = ex.Message;
            }

            return returnValue;
        }

        public async Task<RpcResponse> WriteAsync(string requestId, string method, DriverAddressIoArgModel ioArg)
        {
            try
            {
                if (_webSocket?.State != WebSocketState.Open)
                {
                    return new RpcResponse 
                    { 
                        RequestId = requestId,
                        Method = method,
                        IsSuccess = false,
                        Description = "WebSocket is not connected"
                    };
                }

                var message = new
                {
                    method,
                    value = ioArg.Value,
                    timestamp = DateTime.UtcNow
                };

                var json = System.Text.Json.JsonSerializer.Serialize(message);
                var bytes = Encoding.UTF8.GetBytes(json);

                await _webSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    _cancellationTokenSource?.Token ?? CancellationToken.None);

                return new RpcResponse 
                { 
                    RequestId = requestId,
                    Method = method,
                    IsSuccess = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error writing value {Value} to method {Method}", ioArg.Value, method);
                return new RpcResponse 
                { 
                    RequestId = requestId,
                    Method = method,
                    IsSuccess = false,
                    Description = ex.Message
                };
            }
        }

        public Dictionary<string, AddressDefinitionInfo> GetAddressDefinitions()
        {
            return new Dictionary<string, AddressDefinitionInfo>
            {
                {
                    "temperature",
                    new AddressDefinitionInfo
                    {
                        Description = "温度值",
                        DataType = DataTypeEnum.Double,
                        Unit = "°C"
                    }
                },
                {
                    "device.sensor.value",
                    new AddressDefinitionInfo
                    {
                        Description = "传感器值",
                        DataType = DataTypeEnum.Double,
                        Unit = "unit"
                    }
                },
                {
                    "sensors[0].value",
                    new AddressDefinitionInfo
                    {
                        Description = "传感器数组第一个值",
                        DataType = DataTypeEnum.Double,
                        Unit = "unit"
                    }
                }
            };
        }

        public JToken? GetJsonValue(JToken json, string path)
        {
            try
            {
                return json.SelectToken(path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting JSON value for path {Path}", path);
                return null;
            }
        }

        public void Dispose()
        {
            Close();
            _cancellationTokenSource?.Dispose();
            _webSocket?.Dispose();
        }
    }
}
