using System;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PluginInterface;
using WeightBalance.Models;
using Newtonsoft.Json.Linq;

namespace WeightBalance
{
    [DriverSupported("WeightBalance")]
    [DriverInfo("WeightBalance", "V1.0.0", "Copyright IoTGateway 2024-12-26")]
    public class WeightBalanceDriver : IDriver
    {
        private readonly string _device;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        
        // 连接相关
        private TcpClient _tcpClient;
        private NetworkStream _stream;
        private Task _connectionManagerTask;
        private Task _receiveTask;
        private readonly SemaphoreSlim _connectionLock = new SemaphoreSlim(1, 1);
        private bool _isConnected;
        private bool _isDisposed;
        private DateTime _lastReconnectAttempt = DateTime.MinValue;
        private int _reconnectAttempts = 0;
        private readonly int _maxReconnectAttempts = 5;
        private readonly TimeSpan _reconnectBackoffMultiplier = TimeSpan.FromSeconds(2);
        
        // 数据相关
        private readonly byte[] _buffer = new byte[4096];
        private JObject _latestStatusData = new JObject();
        private JObject _latestResultData = new JObject();
        private DateTime _lastHeartbeatTime = DateTime.MinValue;
        private readonly object _statusLock = new object();
        private readonly object _resultLock = new object();
        
        // 常量
        private const string PRODUCT_TYPE_MPTA5000 = "MPTA-5000";
        
        public ILogger _logger { get; set; }

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

        [ConfigParameter("最小通讯周期")]
        public uint MinPeriod { get; set; } = 1000;

        [ConfigParameter("重连间隔ms")]
        public int ReconnectInterval { get; set; } = 5000;

        [ConfigParameter("心跳间隔ms")]
        public int HeartbeatInterval { get; set; } = 5000;
        
        [ConfigParameter("状态记录间隔ms")]
        public int StatusInterval { get; set; } = 10000;

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

        public bool IsConnected => _isConnected && _tcpClient?.Connected == true;

        public bool Connect()
        {
            // First check if already disposed without acquiring the lock
            if (_isDisposed)
            {
                _logger.LogWarning($"Device:[{_device}],Connect() called after disposal");
                return false;
            }

            bool lockAcquired = false;
            try
            {
                // Try to acquire the lock with a timeout
                lockAcquired = _connectionLock.Wait(StatusInterval);
                
                if (!lockAcquired)
                {
                    _logger.LogError($"Device:[{_device}],Connect() timed out waiting for lock");
                    return false;
                }

                // Double-check disposal state after acquiring the lock
                if (_isDisposed) 
                {
                    _logger.LogWarning($"Device:[{_device}],Connect() abandoned - device disposed after lock acquisition");
                    return false;
                }
                
                if (_isConnected) return true; // Already connected, return success
                
                _logger.LogInformation($"Device:[{_device}],Connecting to {IpAddress}:{Port}");
                
                // Clean up any existing connection
                CleanupConnection(false);
                
                // Create new connection
                _tcpClient = new TcpClient();
                var connectTask = _tcpClient.ConnectAsync(IpAddress, Port);
                
                if (!connectTask.Wait(Timeout))
                {
                    CleanupConnection(false);
                    _logger.LogError($"Device:[{_device}],Connection timeout after {Timeout}ms");
                    return false;
                }
                
                _stream = _tcpClient.GetStream();
                _stream.ReadTimeout = Timeout;
                _stream.WriteTimeout = Timeout;
                
                // Start background tasks
                _isConnected = true;
                _reconnectAttempts = 0;
                _receiveTask = StartReceiveLoop();
                
                // Start connection manager on first successful connection
                if (_connectionManagerTask == null || _connectionManagerTask.IsCompleted)
                {
                    _connectionManagerTask = StartConnectionManager();
                }
                
                _logger.LogInformation($"Device:[{_device}],Connected successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Device:[{_device}],Connect() error: {ex.Message}");
                CleanupConnection(false);
                return false;
            }
            finally
            {
                if (lockAcquired)
                {
                    SafeReleaseLock(_connectionLock);
                }
            }
        }

        public bool Close()
        {
            // First check if already disposed without acquiring the lock
            if (_isDisposed)
            {
                _logger.LogWarning($"Device:[{_device}],Close() called after disposal");
                return false;
            }

            bool lockAcquired = false;
            try
            {
                // Try to acquire the lock with a timeout
                lockAcquired = _connectionLock.Wait(StatusInterval);
                
                if (!lockAcquired)
                {
                    _logger.LogError($"Device:[{_device}],Close() timed out waiting for lock");
                    return false;
                }

                // Double-check disposal state after acquiring the lock
                if (_isDisposed) 
                {
                    _logger.LogWarning($"Device:[{_device}],Close() abandoned - device disposed after lock acquisition");
                    return false;
                }
                
                _logger.LogInformation($"Device:[{_device}],Close()");
                CleanupConnection(false);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Device:[{_device}],Close() error: {ex.Message}");
                return false;
            }
            finally
            {
                if (lockAcquired)
                {
                    SafeReleaseLock(_connectionLock);
                }
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            try
            {
                // Mark as disposed first to prevent new operations
                _isDisposed = true;
                _logger.LogInformation($"Device:[{_device}],Dispose()");
                
                // Try to acquire the lock with timeout to prevent deadlock
                if (!_connectionLock.Wait(TimeSpan.FromSeconds(5)))
                {
                    _logger.LogError($"Device:[{_device}],Timeout waiting for connection lock during Dispose()");
                    // Continue with cleanup even if we can't get the lock
                }
                
                try
                {
                    // Cancel all background tasks
                    _cts.Cancel();
                    
                    // Wait a short time for tasks to end naturally
                    Task.WaitAll(new[] { 
                        Task.Run(() => { if (_connectionManagerTask != null) _connectionManagerTask.Wait(1000); }),
                        Task.Run(() => { if (_receiveTask != null) _receiveTask.Wait(1000); })
                    }, 2000);
                    
                    // Clean up connection resources
                    CleanupConnection(true);
                    
                    // Release other resources
                    _cts.Dispose();
                }
                finally
                {
                    try
                    {
                        // Now we can safely dispose the lock
                        _connectionLock.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Device:[{_device}],Error disposing connection lock");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Device:[{_device}],Dispose() error: {ex.Message}");
            }
        }

        private void CleanupConnection(bool isDisposing)
        {
            _isConnected = false;
            
            if (_stream != null)
            {
                try
                {
                    _stream.Close();
                    _stream.Dispose();
                }
                catch (Exception ex)
                {
                    if (!isDisposing) // 仅在非释放时记录警告，避免释放时的异常日志
                    {
                        _logger.LogWarning(ex, $"Device:[{_device}],Error closing stream");
                    }
                }
                _stream = null;
            }
            
            if (_tcpClient != null)
            {
                try
                {
                    _tcpClient.Close();
                    _tcpClient.Dispose();
                }
                catch (Exception ex)
                {
                    if (!isDisposing)
                    {
                        _logger.LogWarning(ex, $"Device:[{_device}],Error closing TcpClient");
                    }
                }
                _tcpClient = null;
            }
        }

        private Task StartConnectionManager()
        {
            return Task.Run(async () => {
                try
                {
                    while (!_cts.IsCancellationRequested)
                    {
                        try
                        {
                            // 检查连接状态
                            bool needsReconnect = false;
                            bool sendHeartbeat = false;
                            
                            if (_connectionLock.Wait(1000))
                            {
                                try
                                {
                                    // 确认是否需要重连
                                    if (_isConnected && (_tcpClient == null || !_tcpClient.Connected))
                                    {
                                        needsReconnect = true;
                                        _isConnected = false;
                                    }
                                    
                                    // 确认是否需要发送心跳
                                    if (_isConnected && 
                                        (DateTime.Now - _lastHeartbeatTime).TotalMilliseconds >= HeartbeatInterval)
                                    {
                                        sendHeartbeat = true;
                                    }
                                }
                                finally
                                {
                                    SafeReleaseLock(_connectionLock);
                                }
                            }
                            
                            // 心跳检测
                            if (sendHeartbeat)
                            {
                                try
                                {
                                    await SendHeartbeatAsync();
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, $"Device:[{_device}],Heartbeat failed, will try to reconnect");
                                    needsReconnect = true;
                                }
                            }
                            
                            // 处理重连
                            if (needsReconnect)
                            {
                                await HandleReconnect();
                            }
                            
                            // 等待间隔
                            await Task.Delay(1000, _cts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            // 正常取消，退出循环
                            break;
                        }
                        catch (Exception ex)
                        {
                            if (!_isDisposed)
                            {
                                _logger.LogError(ex, $"Device:[{_device}],Connection manager error");
                            }
                            await Task.Delay(1000, _cts.Token); // 错误后稍等一会再继续
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // 正常取消，忽略
                }
                catch (Exception ex)
                {
                    if (!_isDisposed)
                    {
                        _logger.LogError(ex, $"Device:[{_device}],Fatal error in connection manager");
                    }
                }
            }, _cts.Token);
        }

        private async Task HandleReconnect()
        {
            if (_isDisposed) return;
            
            // 实现指数退避重连逻辑
            if (_reconnectAttempts >= _maxReconnectAttempts)
            {
                _logger.LogWarning($"Device:[{_device}],Max reconnect attempts reached ({_maxReconnectAttempts})");
                return;
            }
            
            // 计算等待时间
            var now = DateTime.Now;
            var backoffTime = TimeSpan.FromMilliseconds(ReconnectInterval) * 
                               Math.Pow(_reconnectBackoffMultiplier.TotalSeconds, _reconnectAttempts);
            
            if ((now - _lastReconnectAttempt).TotalMilliseconds < backoffTime.TotalMilliseconds)
            {
                // 未到重连时间
                return;
            }
            
            _lastReconnectAttempt = now;
            _reconnectAttempts++;
            
            _logger.LogInformation($"Device:[{_device}],Reconnect attempt {_reconnectAttempts}/{_maxReconnectAttempts}");
            
            // 尝试重连
            try
            {
                // 我们不直接调用 Connect 方法，而是执行简化版的连接逻辑，以避免锁死
                if (_connectionLock.Wait(StatusInterval))
                {
                    try
                    {
                        if (_isDisposed) return;
                        
                        // 清理已有连接
                        CleanupConnection(false);
                        
                        // 创建新的连接
                        _tcpClient = new TcpClient();
                        await _tcpClient.ConnectAsync(IpAddress, Port);
                        
                        _stream = _tcpClient.GetStream();
                        _stream.ReadTimeout = Timeout;
                        _stream.WriteTimeout = Timeout;
                        
                        // 启动接收循环
                        _isConnected = true;
                        _receiveTask = StartReceiveLoop();
                        
                        _logger.LogInformation($"Device:[{_device}],Reconnected successfully");
                    }
                    finally
                    {
                        SafeReleaseLock(_connectionLock);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Device:[{_device}],Reconnect attempt {_reconnectAttempts} failed");
            }
        }

        private async Task SendHeartbeatAsync()
        {
            if (_isDisposed || !_isConnected) return;
            
            try
            {
                // 使用状态查询作为心跳
                var heartbeatCmd = $"{PRODUCT_TYPE_MPTA5000}:3";
                await SendCommandAsync(heartbeatCmd, false); // 不记录心跳命令
                _lastHeartbeatTime = DateTime.Now;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Device:[{_device}],Heartbeat command failed");
                throw; // 重新抛出异常以触发重连
            }
        }

        private Task StartReceiveLoop()
        {
            return Task.Run(async () => {
                try
                {
                    while (!_cts.IsCancellationRequested && _isConnected && _tcpClient?.Connected == true)
                    {
                        try
                        {
                            // 检查数据可用性并接收
                            if (_stream != null && _stream.DataAvailable)
                            {
                                await ReceiveDataAsync();
                            }
                            
                            await Task.Delay(50, _cts.Token); // 小延迟以避免CPU占用过高
                        }
                        catch (OperationCanceledException)
                        {
                            // 正常取消，退出循环
                            break;
                        }
                        catch (Exception ex)
                        {
                            if (!_isDisposed)
                            {
                                _logger.LogError(ex, $"Device:[{_device}],Error in receive loop");
                                
                                // 通知连接管理器出现问题，需要重连
                                _isConnected = false;
                                break;
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // 正常取消，忽略
                }
                catch (Exception ex)
                {
                    if (!_isDisposed)
                    {
                        _logger.LogError(ex, $"Device:[{_device}],Fatal error in receive loop");
                        _isConnected = false; // 确保连接状态被正确设置
                    }
                }
            }, _cts.Token);
        }

        private async Task ReceiveDataAsync()
        {
            if (_stream == null) return;
            
            try
            {
                int bytesRead = await _stream.ReadAsync(_buffer, 0, _buffer.Length, _cts.Token);
                if (bytesRead > 0)
                {
                    string dataStr = Encoding.UTF8.GetString(_buffer, 0, bytesRead);
                    _logger.LogDebug($"Device:[{_device}],Received: {dataStr}");
                    
                    // 判断数据类型并保存到对应变量
                    if (dataStr.Contains("D_STATES") || dataStr.Contains($"{PRODUCT_TYPE_MPTA5000}:3"))
                    {
                        // 状态数据
                        try
                        {
                            lock (_statusLock)
                            {
                                _latestStatusData = JObject.Parse(dataStr);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, $"Device:[{_device}],Failed to parse status data");
                        }
                    }
                    else if (dataStr.Contains("TEST_RESULT"))
                    {
                        // 结果数据
                        try
                        {
                            lock (_resultLock)
                            {
                                _latestResultData = JObject.Parse(dataStr);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, $"Device:[{_device}],Failed to parse result data");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_isDisposed && !_cts.IsCancellationRequested)
                {
                    _logger.LogError(ex, $"Device:[{_device}],Error receiving data");
                    throw; // 重新抛出异常以便上层处理连接问题
                }
            }
        }

        private void SafeReleaseLock(SemaphoreSlim semaphore)
        {
            try
            {
                if (semaphore != null && !_isDisposed)
                {
                    semaphore.Release();
                }
            }
            catch (ObjectDisposedException)
            {
                // Ignore already disposed object
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Device:[{_device}],Error releasing lock");
            }
        }

        private async Task<string> SendCommandAsync(string command, bool logCommand = true)
        {
            // First check if already disposed without acquiring the lock
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(WeightBalanceDriver));
            }

            if (!_isConnected || _tcpClient == null || !_tcpClient.Connected)
            {
                throw new InvalidOperationException("设备未连接");
            }

            if (logCommand)
            {
                _logger.LogDebug($"Device:[{_device}],Sending: {command}");
            }
            
            // Try multiple times to send, until success or retry limit
            for (int attempt = 0; attempt < CommandRetries; attempt++)
            {
                bool lockAcquired = false;
                try
                {
                    // Try to acquire the lock with a timeout
                    lockAcquired = _connectionLock.Wait(StatusInterval);
                    
                    if (!lockAcquired)
                    {
                        throw new TimeoutException("获取连接锁超时");
                    }
                    
                    // Double-check disposal state after acquiring the lock
                    if (_isDisposed) 
                    {
                        throw new ObjectDisposedException(nameof(WeightBalanceDriver));
                    }
                    
                    // Check connection state again after acquiring lock
                    if (!_isConnected || _tcpClient == null || !_tcpClient.Connected)
                    {
                        throw new InvalidOperationException("设备未连接");
                    }
                    
                    byte[] data = Encoding.UTF8.GetBytes(command);
                    await _stream.WriteAsync(data, 0, data.Length, _cts.Token);
                    await _stream.FlushAsync(_cts.Token);
                    
                    // Success - return immediately
                    return command;
                }
                catch (Exception ex)
                {
                    // Last attempt failed, throw exception
                    if (attempt == CommandRetries - 1) throw;
                    
                    _logger.LogWarning(ex, $"Device:[{_device}],Command failed, retrying ({attempt+1}/{CommandRetries})");
                    await Task.Delay(RetryInterval);
                }
                finally
                {
                    if (lockAcquired)
                    {
                        SafeReleaseLock(_connectionLock);
                    }
                }
            }

            // Should never reach here since the last attempt would throw an exception
            throw new InvalidOperationException("发送命令失败");
        }

        [Method("读取状态", description: "读取定重心台状态")]
        public DriverReturnValueModel Read(DriverAddressIoArgModel ioarg)
        {
            var ret = new DriverReturnValueModel { StatusType = VaribaleStatusTypeEnum.Good };
            
            if (_isDisposed)
            {
                ret.StatusType = VaribaleStatusTypeEnum.Bad;
                ret.Message = "设备已释放";
                return ret;
            }
            
            if (!IsConnected)
            {
                ret.StatusType = VaribaleStatusTypeEnum.Bad;
                ret.Message = "设备未连接";
                return ret;
            }
            
            try
            {
                lock (_statusLock)
                {
                    if (_latestStatusData == null || _latestStatusData.Count == 0)
                    {
                        ret.StatusType = VaribaleStatusTypeEnum.Bad;
                        ret.Message = "没有可用的状态数据";
                        return ret;
                    }
                    
                    // 处理JSON路径解析
                    if (!string.IsNullOrEmpty(ioarg.Address))
                    {
                        JToken token = _latestStatusData.SelectToken(ioarg.Address);
                        if (token == null)
                        {
                            ret.StatusType = VaribaleStatusTypeEnum.Bad;
                            ret.Message = $"未找到JSON路径: {ioarg.Address}";
                            return ret;
                        }
                        ret.Value = ConvertJTokenToValue(token);
                    }
                    else
                    {
                        ret.Value = _latestStatusData.ToString();
                    }
                    return ret;
                }
            }
            catch (Exception ex)
            {
                ret.StatusType = VaribaleStatusTypeEnum.Bad;
                ret.Message = ex.Message;
                _logger.LogError(ex, $"Device:[{_device}],读取状态数据错误");
                return ret;
            }
        }

        [Method("读取结果", description: "读取定重心台结果")]
        public DriverReturnValueModel ReadResult(DriverAddressIoArgModel ioarg)
        {
            var ret = new DriverReturnValueModel { StatusType = VaribaleStatusTypeEnum.Good };
            
            if (_isDisposed)
            {
                ret.StatusType = VaribaleStatusTypeEnum.Bad;
                ret.Message = "设备已释放";
                return ret;
            }
            
            if (!IsConnected)
            {
                ret.StatusType = VaribaleStatusTypeEnum.Bad;
                ret.Message = "设备未连接";
                return ret;
            }
            
            try
            {                
                lock (_resultLock)
                {
                    if (_latestResultData == null || _latestResultData.Count == 0)
                    {
                        ret.StatusType = VaribaleStatusTypeEnum.Bad;
                        ret.Message = "没有可用的结果数据";
                        return ret;
                    }

                    // 处理JSON路径解析
                    if (!string.IsNullOrEmpty(ioarg.Address))
                    {
                        JToken token = _latestResultData.SelectToken(ioarg.Address);
                        if (token == null)
                        {
                            ret.StatusType = VaribaleStatusTypeEnum.Bad;
                            ret.Message = $"未找到JSON路径: {ioarg.Address}";
                            return ret;
                        }
                        ret.Value = ConvertJTokenToValue(token);
                    }
                    else
                    {
                        ret.Value = _latestResultData.ToString();
                    }
                    return ret;
                }
            }
            catch (Exception ex)
            {
                ret.StatusType = VaribaleStatusTypeEnum.Bad;
                ret.Message = ex.Message;
                _logger.LogError(ex, $"Device:[{_device}],读取结果数据错误");
                return ret;
            }
        }

        private object ConvertJTokenToValue(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.String:
                    return token.Value<string>();
                case JTokenType.Integer:
                    return token.Value<long>();
                case JTokenType.Float:
                    return token.Value<double>();
                case JTokenType.Boolean:
                    return token.Value<bool>();
                case JTokenType.Date:
                    return token.Value<DateTime>();
                case JTokenType.Null:
                    return null;
                case JTokenType.Object:
                case JTokenType.Array:
                    return token.ToString();
                default:
                    return token.ToString();
            }
        }

        public async Task<RpcResponse> WriteAsync(string requestId, string method, DriverAddressIoArgModel ioarg)
        {
            if (_isDisposed)
            {
                return new RpcResponse { IsSuccess = false, Description = "设备已释放" };
            }

            try
            {
                if (!IsConnected)
                {
                    return new RpcResponse { IsSuccess = false, Description = "设备未连接" };
                }

                return new RpcResponse { IsSuccess = false, Description = "不支持的命令格式" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Device:[{_device}],WriteAsync error");
                return new RpcResponse { IsSuccess = false, Description = ex.Message };
            }
        }
    }
}