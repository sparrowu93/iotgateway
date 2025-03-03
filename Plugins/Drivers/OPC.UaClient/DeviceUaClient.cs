using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using PluginInterface;

namespace DriverOpcUaClient
{
    [DriverSupported("OpcUaDevice")]
    [DriverInfo("OpcUaClient", "V1.0.0", "Copyright iotgateway 2022-06-04")]
    public class OpcUaClient : IDriver
    {
        private Session _session;
        private SessionReconnectHandler _reconnectHandler;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        private bool _keepRunning;
        private Task _monitoringTask;
        private CancellationTokenSource _cts;
        private bool _isDisposed = false;
        
        public ILogger _logger { get; set; }

        public int NamespaceIndex { get; set; } = 2;
        private readonly string _device;
        
        #region 配置参数

        [ConfigParameter("设备Id")] 
        public string DeviceId { get; set; }

        [ConfigParameter("服务器地址")]
        public string ServerUrl { get; set; } = "opc.tcp://localhost:4840";
        
        [ConfigParameter("安全模式")]
        public SecurityMode SecurityMode { get; set; } = SecurityMode.None;
        
        [ConfigParameter("安全策略")]
        public string SecurityPolicy { get; set; } = SecurityPolicies.None;
        
        [ConfigParameter("用户名")]
        public string Username { get; set; } = string.Empty;
        
        [ConfigParameter("密码")]
        public string Password { get; set; } = string.Empty;
        
        [ConfigParameter("超时时间ms")]
        public int Timeout { get; set; } = 5000;
        
        [ConfigParameter("会话生命周期ms")]
        public int SessionLifetime { get; set; } = 60000;
        
        [ConfigParameter("最小通讯周期ms")]
        public uint MinPeriod { get; set; } = 3000;
        
        [ConfigParameter("重连间隔ms")]
        public int ReconnectPeriod { get; set; } = 10000;
        
        #endregion
        
        public OpcUaClient(string device, ILogger logger)
        {
            _device = device;
            _logger = logger;
            
            _logger.LogInformation($"Device:[{_device}],Create()");
        }
        
        public bool IsConnected => _session != null && _session.Connected;
        
        public bool Connect()
        {
            try
            {
                _logger.LogInformation($"Device:[{_device}],Connect() - 正在连接到 {ServerUrl}");
                
                // 初始化应用配置
                var config = CreateApplicationConfiguration();
                
                // 同步执行验证
                config.Validate(ApplicationType.Client).GetAwaiter().GetResult();
                
                // 确保证书存在
                bool certOk = EnsureApplicationCertificate(config);
                if (!certOk)
                {
                    _logger.LogError($"Device:[{_device}],无法创建或找到应用证书");
                    return false;
                }
                
                // 选择终结点
                var selectedEndpoint = CoreClientUtils.SelectEndpoint(ServerUrl, false, Timeout);
                if (selectedEndpoint == null)
                {
                    _logger.LogError($"Device:[{_device}],无法找到服务器 {ServerUrl} 的终结点");
                    return false;
                }
                
                // 设置安全模式和策略
                selectedEndpoint.SecurityMode = (MessageSecurityMode)SecurityMode;
                selectedEndpoint.SecurityPolicyUri = SecurityPolicy;
                
                _logger.LogInformation($"Device:[{_device}],使用终结点: {selectedEndpoint.EndpointUrl}, 安全模式: {selectedEndpoint.SecurityMode}, 安全策略: {selectedEndpoint.SecurityPolicyUri}");
                
                // 创建用户身份
                IUserIdentity userIdentity = new UserIdentity();
                if (!string.IsNullOrEmpty(Username))
                {
                    userIdentity = new UserIdentity(Username, Password);
                }
                
                // 创建会话
                var endpointConfiguration = EndpointConfiguration.Create(config);
                var endpoint = new ConfiguredEndpoint(null, selectedEndpoint, endpointConfiguration);
                
                _session = Session.Create(
                    config,
                    endpoint,
                    true,
                    "DriverOpcUaClient",
                    (uint)Timeout,
                    userIdentity,
                    null).GetAwaiter().GetResult();
                
                // 设置会话事件
                _session.KeepAlive += Session_KeepAlive;
                
                // 启动监控任务
                _cts = new CancellationTokenSource();
                _keepRunning = true;
                _monitoringTask = Task.Run(() => MonitorSessionAsync(_cts.Token));
                
                _logger.LogInformation($"Device:[{_device}],已成功连接到 {ServerUrl}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Device:[{_device}],连接错误: {ex.Message}");
                return false;
            }
        }
        
        private ApplicationConfiguration CreateApplicationConfiguration()
        {
            return new ApplicationConfiguration()
            {
                ApplicationName = $"OpcUaClient_{_device}",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier(),
                    TrustedPeerCertificates = new CertificateTrustList(),
                    TrustedIssuerCertificates = new CertificateTrustList(),
                    RejectedCertificateStore = new CertificateTrustList(),
                    AutoAcceptUntrustedCertificates = true,
                    RejectSHA1SignedCertificates = false
                },
                TransportQuotas = new TransportQuotas
                {
                    OperationTimeout = Timeout,
                    MaxStringLength = 1024 * 1024,
                    MaxByteStringLength = 1024 * 1024,
                    MaxArrayLength = 65535,
                    MaxMessageSize = 1024 * 1024,
                    MaxBufferSize = 65535,
                    ChannelLifetime = 300000,
                    SecurityTokenLifetime = 3600000
                },
                ClientConfiguration = new ClientConfiguration
                {
                    DefaultSessionTimeout = SessionLifetime,
                    MinSubscriptionLifetime = 10000
                },
                TraceConfiguration = new TraceConfiguration()
            };
        }
        
        private bool EnsureApplicationCertificate(ApplicationConfiguration config)
        {
            try
            {
                // 同步执行验证
                config.Validate(ApplicationType.Client).GetAwaiter().GetResult();
                
                // 查找有效证书
                X509Certificate2 certificate = config.SecurityConfiguration.ApplicationCertificate.Find(true).GetAwaiter().GetResult();
                if (certificate == null)
                {
                    // 创建自签名证书 - 同步调用
                    X509Certificate2 newCertificate = CertificateFactory.CreateCertificate(
                        config.SecurityConfiguration.ApplicationCertificate.StoreType,
                        config.SecurityConfiguration.ApplicationCertificate.StorePath,
                        null,
                        config.ApplicationUri,
                        config.ApplicationName,
                        config.SecurityConfiguration.ApplicationCertificate.SubjectName,
                        null,
                        CertificateFactory.DefaultKeySize,
                        DateTime.UtcNow - TimeSpan.FromDays(1),
                        12,
                        CertificateFactory.DefaultHashSize,
                        false,
                        null,
                        null);
                    
                    // 更新配置中的证书
                    config.SecurityConfiguration.ApplicationCertificate.Certificate = newCertificate;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Device:[{_device}],证书错误: {ex.Message}");
                return false;
            }
        }
        
        private void Session_KeepAlive(Session session, KeepAliveEventArgs e)
        {
            try
            {
                if (e.Status != null && ServiceResult.IsNotGood(e.Status))
                {
                    _logger.LogWarning($"Device:[{_device}],会话保活错误: {e.Status}");
                    
                    if (_reconnectHandler == null)
                    {
                        _logger.LogInformation($"Device:[{_device}],开始重连...");
                        _reconnectHandler = new SessionReconnectHandler();
                        _reconnectHandler.BeginReconnect(_session, ReconnectPeriod, Session_Reconnected);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Device:[{_device}],保活处理错误: {ex.Message}");
            }
        }
        
        private void Session_Reconnected(object sender, EventArgs e)
        {
            try
            {
                if (_reconnectHandler != null)
                {
                    _session = _reconnectHandler.Session;
                    _reconnectHandler.Dispose();
                    _reconnectHandler = null;
                    _logger.LogInformation($"Device:[{_device}],已重新连接到 {ServerUrl}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Device:[{_device}],重连处理错误: {ex.Message}");
            }
        }
        
        private async Task MonitorSessionAsync(CancellationToken token)
        {
            while (_keepRunning && !token.IsCancellationRequested)
            {
                try
                {
                    if (!IsConnected && _reconnectHandler == null)
                    {
                        _logger.LogWarning($"Device:[{_device}],会话断开，尝试重新连接");
                        Connect();
                    }
                    
                    await Task.Delay(ReconnectPeriod, token);
                }
                catch (TaskCanceledException)
                {
                    // 正常取消，不做处理
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Device:[{_device}],监控会话错误: {ex.Message}");
                    await Task.Delay(5000, token);
                }
            }
        }
        
        public bool Close()
        {
            _logger.LogInformation($"Device:[{_device}],Close()");
            try
            {
                _keepRunning = false;
                
                if (_cts != null)
                {
                    _cts.Cancel();
                    try 
                    {
                        _monitoringTask?.Wait(1000);
                    } 
                    catch 
                    {
                        // 忽略任务取消异常
                    }
                }
                
                if (_session != null)
                {
                    _session.KeepAlive -= Session_KeepAlive;
                    _session.Close();
                    _session = null;
                }
                
                if (_reconnectHandler != null)
                {
                    _reconnectHandler.Dispose();
                    _reconnectHandler = null;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Device:[{_device}],Close错误: {ex.Message}");
                return false;
            }
        }
        
        public void Dispose()
        {
            _logger.LogInformation($"Device:[{_device}],Dispose()");
            try
            {
                // Mark as disposed first to prevent new operations
                _isDisposed = true;
                
                // Close the device
                Close();
                
                // Clean up the CTS
                _cts?.Dispose();
                _cts = null;
                
                // Now we can safely dispose the lock
                _lock?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Device:[{_device}],Dispose错误: {ex.Message}");
            }
        }
        
        [Method("读取节点值", description: "读取指定节点的值,Address格式为NodeId")]
        public DriverReturnValueModel Read(DriverAddressIoArgModel ioarg)
        {
            var ret = new DriverReturnValueModel { StatusType = VaribaleStatusTypeEnum.Good };
            
            // Check if already disposed before waiting on the lock
            if (_isDisposed)
            {
                ret.StatusType = VaribaleStatusTypeEnum.Bad;
                ret.Message = "设备已被释放";
                _logger.LogWarning($"Device:[{_device}],尝试在已释放的设备上读取");
                return ret;
            }
            
            bool lockAcquired = false;
            
            try
            {
                // Try to acquire the lock with a timeout
                lockAcquired = _lock.Wait(2000); // 2 second timeout
                
                if (!lockAcquired)
                {
                    ret.StatusType = VaribaleStatusTypeEnum.Bad;
                    ret.Message = "获取设备锁超时";
                    _logger.LogWarning($"Device:[{_device}],获取设备锁超时");
                    return ret;
                }
                
                // Check again after acquiring the lock
                if (_isDisposed)
                {
                    ret.StatusType = VaribaleStatusTypeEnum.Bad;
                    ret.Message = "设备已被释放";
                    _logger.LogWarning($"Device:[{_device}],设备已被释放，无法读取");
                    return ret;
                }
                
                if (!IsConnected)
                {
                    ret.StatusType = VaribaleStatusTypeEnum.Bad;
                    ret.Message = "OPC UA服务器未连接";
                    return ret;
                }
                
                // 解析nodeId
                NodeId nodeId = ParseNodeId(ioarg.Address);
                
                // 读取节点值
                DataValue value = _session.ReadValue(nodeId);
                
                // 检查读取状态
                if (StatusCode.IsGood(value.StatusCode))
                {
                    // 转换值为请求的数据类型
                    ret.Value = ConvertValueToType(value.Value, ioarg.ValueType);
                }
                else
                {
                    ret.StatusType = VaribaleStatusTypeEnum.Bad;
                    ret.Message = $"读取失败: {value.StatusCode}";
                    _logger.LogWarning($"Device:[{_device}],读取节点 {nodeId} 失败: {value.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                ret.StatusType = VaribaleStatusTypeEnum.Bad;
                ret.Message = $"读取异常: {ex.Message}";
                _logger.LogError($"Device:[{_device}],Read错误: {ex.Message}");
            }
            finally
            {
                // Only release if we acquired the lock and the instance hasn't been disposed
                if (lockAcquired && !_isDisposed)
                {
                    try
                    {
                        _lock.Release();
                    }
                    catch (ObjectDisposedException)
                    {
                        // This might happen in rare race conditions, just log it
                        _logger.LogWarning($"Device:[{_device}],释放已处置的锁");
                    }
                }
            }
            
            return ret;
        }
        
        [Method("批量读取节点值", description: "批量读取节点值,Address格式为NodeId列表,逗号分隔")]
        public DriverReturnValueModel ReadMultiple(DriverAddressIoArgModel ioarg)
        {
            var ret = new DriverReturnValueModel { StatusType = VaribaleStatusTypeEnum.Good };
            
            // Check if already disposed before waiting on the lock
            if (_isDisposed)
            {
                ret.StatusType = VaribaleStatusTypeEnum.Bad;
                ret.Message = "设备已被释放";
                _logger.LogWarning($"Device:[{_device}],尝试在已释放的设备上批量读取");
                return ret;
            }
            
            bool lockAcquired = false;
            
            try
            {
                // Try to acquire the lock with a timeout
                lockAcquired = _lock.Wait(2000); // 2 second timeout
                
                if (!lockAcquired)
                {
                    ret.StatusType = VaribaleStatusTypeEnum.Bad;
                    ret.Message = "获取设备锁超时";
                    _logger.LogWarning($"Device:[{_device}],获取设备锁超时");
                    return ret;
                }
                
                // Check again after acquiring the lock
                if (_isDisposed)
                {
                    ret.StatusType = VaribaleStatusTypeEnum.Bad;
                    ret.Message = "设备已被释放";
                    _logger.LogWarning($"Device:[{_device}],设备已被释放，无法批量读取");
                    return ret;
                }
                
                if (!IsConnected)
                {
                    ret.StatusType = VaribaleStatusTypeEnum.Bad;
                    ret.Message = "OPC UA服务器未连接";
                    return ret;
                }
                
                // 解析多个地址(逗号分隔)
                string[] addresses = ioarg.Address.Split(',');
                List<NodeId> nodeIds = new List<NodeId>();
                
                foreach (var address in addresses)
                {
                    try
                    {
                        NodeId nodeId = ParseNodeId(address.Trim());
                        nodeIds.Add(nodeId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Device:[{_device}],无效的节点ID: {address}, 错误: {ex.Message}");
                    }
                }
                
                if (nodeIds.Count == 0)
                {
                    ret.StatusType = VaribaleStatusTypeEnum.Bad;
                    ret.Message = "没有有效的节点ID";
                    return ret;
                }
                
                // 准备读取值集合
                ReadValueIdCollection nodesToRead = new ReadValueIdCollection();
                foreach (var nodeId in nodeIds)
                {
                    nodesToRead.Add(new ReadValueId() { NodeId = nodeId, AttributeId = Attributes.Value });
                }
                
                // 读取所有节点
                DataValueCollection results;
                DiagnosticInfoCollection diagnosticInfos;
                 
                _session.Read(
                    null,
                    0,
                    TimestampsToReturn.Both,
                    nodesToRead,
                    out results,
                    out diagnosticInfos);
                
                // 检查所有读取是否成功
                bool allGood = true;
                var values = new List<object>();
                var resultDict = new Dictionary<string, object>();
                
                for (int i = 0; i < results.Count; i++)
                {
                    if (StatusCode.IsGood(results[i].StatusCode))
                    {
                        object convertedValue = ConvertValueToType(results[i].Value, ioarg.ValueType);
                        values.Add(convertedValue);
                        resultDict[nodeIds[i].ToString()] = convertedValue;
                    }
                    else
                    {
                        allGood = false;
                        _logger.LogWarning($"Device:[{_device}],读取NodeId {nodeIds[i]}失败: {results[i].StatusCode}");
                        values.Add(null);
                        resultDict[nodeIds[i].ToString()] = null;
                    }
                }
                
                ret.Value = resultDict;
                if (!allGood)
                {
                    ret.StatusType = VaribaleStatusTypeEnum.Bad;
                    ret.Message = "部分读取失败，请查看日志";
                }
            }
            catch (Exception ex)
            {
                ret.StatusType = VaribaleStatusTypeEnum.Bad;
                ret.Message = $"批量读取异常: {ex.Message}";
                _logger.LogError($"Device:[{_device}],ReadMultiple错误: {ex.Message}");
            }
            finally
            {
                // Only release if we acquired the lock and the instance hasn't been disposed
                if (lockAcquired && !_isDisposed)
                {
                    try
                    {
                        _lock.Release();
                    }
                    catch (ObjectDisposedException)
                    {
                        // This might happen in rare race conditions, just log it
                        _logger.LogWarning($"Device:[{_device}],释放已处置的锁");
                    }
                }
            }
            
            return ret;
        }
        
        [Method("浏览节点", description: "浏览指定节点下的子节点,Address格式为起始NodeId")]
        public DriverReturnValueModel Browse(DriverAddressIoArgModel ioarg)
        {
            var ret = new DriverReturnValueModel { StatusType = VaribaleStatusTypeEnum.Good };
            
            // Check if already disposed before waiting on the lock
            if (_isDisposed)
            {
                ret.StatusType = VaribaleStatusTypeEnum.Bad;
                ret.Message = "设备已被释放";
                _logger.LogWarning($"Device:[{_device}],尝试在已释放的设备上浏览节点");
                return ret;
            }
            
            bool lockAcquired = false;
            
            try
            {
                // Try to acquire the lock with a timeout
                lockAcquired = _lock.Wait(2000); // 2 second timeout
                
                if (!lockAcquired)
                {
                    ret.StatusType = VaribaleStatusTypeEnum.Bad;
                    ret.Message = "获取设备锁超时";
                    _logger.LogWarning($"Device:[{_device}],获取设备锁超时");
                    return ret;
                }
                
                // Check again after acquiring the lock
                if (_isDisposed)
                {
                    ret.StatusType = VaribaleStatusTypeEnum.Bad;
                    ret.Message = "设备已被释放";
                    _logger.LogWarning($"Device:[{_device}],设备已被释放，无法浏览节点");
                    return ret;
                }
                
                if (!IsConnected)
                {
                    ret.StatusType = VaribaleStatusTypeEnum.Bad;
                    ret.Message = "OPC UA服务器未连接";
                    return ret;
                }
                
                // 设置默认起点
                NodeId startNodeId;
                if (string.IsNullOrEmpty(ioarg.Address))
                {
                    startNodeId = ObjectIds.RootFolder;
                }
                else
                {
                    try
                    {
                        startNodeId = ParseNodeId(ioarg.Address);
                    }
                    catch (Exception)
                    {
                        _logger.LogWarning($"Device:[{_device}],无效的起始节点ID: {ioarg.Address}, 使用RootFolder代替");
                        startNodeId = ObjectIds.RootFolder;
                    }
                }
                
                // 浏览服务器
                var browser = new Browser(_session)
                {
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true,
                    NodeClassMask = (int)(NodeClass.Object | NodeClass.Variable),
                    ResultMask = (uint)BrowseResultMask.All
                };
                
                ReferenceDescriptionCollection references = browser.Browse(startNodeId);
                
                // 转换为更简单的格式
                List<Dictionary<string, object>> nodes = new List<Dictionary<string, object>>();
                
                foreach (var reference in references)
                {
                    nodes.Add(new Dictionary<string, object>
                    {
                        { "NodeId", reference.NodeId.ToString() },
                        { "DisplayName", reference.DisplayName.Text },
                        { "BrowseName", reference.BrowseName.ToString() },
                        { "NodeClass", reference.NodeClass.ToString() }
                    });
                }
                
                ret.Value = nodes;
            }
            catch (Exception ex)
            {
                ret.StatusType = VaribaleStatusTypeEnum.Bad;
                ret.Message = $"浏览节点异常: {ex.Message}";
                _logger.LogError($"Device:[{_device}],Browse错误: {ex.Message}");
            }
            finally
            {
                // Only release if we acquired the lock and the instance hasn't been disposed
                if (lockAcquired && !_isDisposed)
                {
                    try
                    {
                        _lock.Release();
                    }
                    catch (ObjectDisposedException)
                    {
                        // This might happen in rare race conditions, just log it
                        _logger.LogWarning($"Device:[{_device}],释放已处置的锁");
                    }
                }
            }
            
            return ret;
        }
        
        [Method("写入节点值", description: "写入节点值,Address格式为NodeId")]
        public async Task<RpcResponse> WriteAsync(string requestId, string method, DriverAddressIoArgModel ioarg)
        {
            RpcResponse rpcResponse = new() { IsSuccess = false, Description = "正在处理写入请求" };
            
            // Check if already disposed before waiting on the lock
            if (_isDisposed)
            {
                rpcResponse.Description = "设备已被释放";
                _logger.LogWarning($"Device:[{_device}],尝试在已释放的设备上写入");
                return rpcResponse;
            }
            
            bool lockAcquired = false;
            
            try
            {
                // Try to acquire the lock with a timeout
                lockAcquired = await _lock.WaitAsync(2000); // 2 second timeout
                
                if (!lockAcquired)
                {
                    rpcResponse.Description = "获取设备锁超时";
                    _logger.LogWarning($"Device:[{_device}],获取设备锁超时");
                    return rpcResponse;
                }
                
                // Check again after acquiring the lock
                if (_isDisposed)
                {
                    rpcResponse.Description = "设备已被释放";
                    _logger.LogWarning($"Device:[{_device}],设备已被释放，无法写入");
                    return rpcResponse;
                }
                
                if (!IsConnected)
                {
                    rpcResponse.Description = "OPC UA服务器未连接";
                    return rpcResponse;
                }
                
                // 解析参数
                if (method == "Write")
                {
                    // 解析nodeId
                    NodeId nodeId;
                    try
                    {
                        nodeId = ParseNodeId(ioarg.Address);
                    }
                    catch (Exception ex)
                    {
                        rpcResponse.Description = $"无效的节点ID格式: {ex.Message}";
                        return rpcResponse;
                    }
                    
                    // 转换值为适当的类型
                    Variant value = new Variant(ioarg.Value);
                    
                    // 写入服务器
                    WriteValue nodeToWrite = new WriteValue
                    {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(value)
                    };
                    
                    WriteValueCollection nodesToWrite = new WriteValueCollection { nodeToWrite };
                    
                    // 执行写入操作
                    StatusCodeCollection results;
                    DiagnosticInfoCollection diagnosticInfos;
                    
                    ResponseHeader responseHeader = _session.Write(
                        null,
                        nodesToWrite,
                        out results,
                        out diagnosticInfos);
                    
                    // 检查结果
                    if (results.Count > 0 && StatusCode.IsGood(results[0]))
                    {
                        rpcResponse.IsSuccess = true;
                        rpcResponse.Description = "写入成功";
                    }
                    else
                    {
                        rpcResponse.Description = $"写入失败: {(results.Count > 0 ? results[0].ToString() : "未知错误")}";
                    }
                }
                else
                {
                    rpcResponse.Description = $"未知的方法: {method}";
                }
            }
            catch (Exception ex)
            {
                rpcResponse.Description = $"写入异常: {ex.Message}";
                _logger.LogError($"Device:[{_device}],Write错误: {ex.Message}");
            }
            finally
            {
                // Only release if we acquired the lock and the instance hasn't been disposed
                if (lockAcquired && !_isDisposed)
                {
                    try
                    {
                        _lock.Release();
                    }
                    catch (ObjectDisposedException)
                    {
                        // This might happen in rare race conditions, just log it
                        _logger.LogWarning($"Device:[{_device}],释放已处置的锁");
                    }
                }
            }
            
            return rpcResponse;
        }
        
        // 将OPC UA值转换为请求的类型
        private object ConvertValueToType(object value, DataTypeEnum valueType)
        {
            try
            {
                if (value == null)
                {
                    return null;
                }
                
                switch (valueType)
                {
                    case DataTypeEnum.UByte:
                        return Convert.ToByte(value);
                    case DataTypeEnum.Byte:
                        return Convert.ToSByte(value);
                    case DataTypeEnum.Int16:
                        return Convert.ToInt16(value);
                    case DataTypeEnum.Uint16:
                        return Convert.ToUInt16(value);
                    case DataTypeEnum.Int32:
                        return Convert.ToInt32(value);
                    case DataTypeEnum.Uint32:
                        return Convert.ToUInt32(value);
                    case DataTypeEnum.Int64:
                        return Convert.ToInt64(value);
                    case DataTypeEnum.Uint64:
                        return Convert.ToUInt64(value);
                    case DataTypeEnum.Float:
                        return Convert.ToSingle(value);
                    case DataTypeEnum.Double:
                        return Convert.ToDouble(value);
                    case DataTypeEnum.Bool:
                        return Convert.ToBoolean(value);
                    case DataTypeEnum.Utf8String:
                        return value.ToString();
                    case DataTypeEnum.DateTime:
                        return Convert.ToDateTime(value);
                    default:
                        return value;
                }
            }
            catch
            {
                // 如果转换失败，返回原始值
                return value;
            }
        }
        
        // 解析NodeId字符串为NodeId对象
        private NodeId ParseNodeId(string nodeIdString)
        {
            if (string.IsNullOrEmpty(nodeIdString))
            {
                throw new ArgumentException("节点ID不能为空");
            }
            
            // 直接尝试解析
            try
            {
                return NodeId.Parse(nodeIdString);
            }
            catch
            {
                // 如果不是标准格式，假设是字符串标识符并添加命名空间
                return new NodeId(nodeIdString, (ushort)NamespaceIndex);
            }
        }
    }

    public enum SecurityMode
    {
        None = 1,
        Sign = 2,
        SignAndEncrypt = 3
    }
}