using Opc.Ua;
using OpcUaHelper;
using PluginInterface;
using Microsoft.Extensions.Logging;
using OPC.UaClient.Models;

namespace OPC.UaClient
{
    [DriverSupported("OPCUaClient")]
    [DriverInfo("OPCUaClient", "V1.0.0", "Copyright IoTGateway.net 20230220")]
    public class DeviceUaClient : IDriver, IAddressDefinitionProvider
    {
        private OpcUaClientHelper? _opcUaClient;
        private readonly string _device;
        private bool _isConnected;
        private ApplicationConfiguration? _clientConfig;

        public ILogger _logger { get; set; }

        #region 配置参数

        [ConfigParameter("设备Id")] public string DeviceId { get; set; }

        [ConfigParameter("服务器地址")]
        public string Uri { get; set; } = "opc.tcp://localhost:62541/Quickstarts/ReferenceServer";

        [ConfigParameter("超时时间ms")] public int Timeout { get; set; } = 3000;

        [ConfigParameter("最小通讯周期ms")] public uint MinPeriod { get; set; } = 3000;

        [ConfigParameter("使用安全连接")] public bool UseSecurity { get; set; } = false;

        #endregion

        #region 生命周期

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="device"></param>
        /// <param name="logger"></param>
        public DeviceUaClient(string device, ILogger<DeviceUaClient> logger)
        {
            _device = device;
            _logger = logger;

            _logger.LogInformation($"Device:[{_device}],Create()");
        }

        /// <summary>
        /// 设置客户端配置
        /// </summary>
        /// <param name="config">OPC UA 客户端配置</param>
        public void SetClientConfiguration(ApplicationConfiguration config)
        {
            _clientConfig = config;
            if (_opcUaClient != null)
            {
                _opcUaClient.SetClientConfiguration(config);
            }
        }

        /// <summary>
        /// 连接状态
        /// </summary>
        public bool IsConnected => _isConnected;

        /// <summary>
        /// 连接
        /// </summary>
        /// <returns></returns>
        public bool Connect()
        {
            try
            {
                _logger.LogInformation($"Device:[{_device}],Connect() to {Uri}");

                _opcUaClient = new OpcUaClientHelper();
                if (_clientConfig != null)
                {
                    _opcUaClient.SetClientConfiguration(_clientConfig);
                }
                var connectTask = _opcUaClient.ConnectServer(Uri, UseSecurity);
                
                if (!connectTask.Wait(60000)) // 使用与 OpcUaClientHelper 相同的超时时间
                {
                    _logger.LogError($"Device:[{_device}],Connect() timeout after 60 seconds");
                    return false;
                }
                _isConnected = true;
            }
            catch (AggregateException ex)
            {
                var innerException = ex.InnerException;
                _logger.LogError(innerException, $"Device:[{_device}],Connect() failed: {innerException?.Message}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Device:[{_device}],Connect() failed: {ex.Message}");
                return false;
            }

            return IsConnected;
        }

        /// <summary>
        /// 断开
        /// </summary>
        /// <returns></returns>
        public bool Close()
        {
            try
            {
                _logger.LogInformation($"Device:[{_device}],Close()");

                _opcUaClient?.Disconnect();
                _isConnected = false;
                return !IsConnected;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Device:[{_device}],Close(),Error");
                return false;
            }
        }

        /// <summary>
        /// 释放
        /// </summary>
        public void Dispose()
        {
            try
            {
                _logger.LogInformation($"Device:[{_device}],Dispose()");
                _opcUaClient = null;

                // Suppress finalization.
                GC.SuppressFinalize(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Device:[{_device}],Dispose(),Error");
            }
        }



        #endregion

        [Method("读OPCUa", description: "读OPCUa节点")]
        public DriverReturnValueModel ReadNode(DriverAddressIoArgModel ioArg)
        {
            var ret = new DriverReturnValueModel { StatusType = VaribaleStatusTypeEnum.Good };

            if (!IsConnected)
            {
                ret.StatusType = VaribaleStatusTypeEnum.Bad;
                ret.Message = "连接失败";
                _logger.LogError($"Device:[{_device}],ReadNode({ioArg.Address}) failed: not connected");
                return ret;
            }

            try
            {
                if (_opcUaClient == null)
                {
                    ret.StatusType = VaribaleStatusTypeEnum.Bad;
                    ret.Message = "OPC UA 客户端未初始化";
                    _logger.LogError($"Device:[{_device}],ReadNode({ioArg.Address}) failed: client not initialized");
                    return ret;
                }

                _logger.LogInformation($"Device:[{_device}],ReadNode({ioArg.Address}) attempting to read node");
                var nodeId = new NodeId(ioArg.Address);
                var dataValue = _opcUaClient.ReadNode(nodeId);

                if (dataValue == null)
                {
                    ret.StatusType = VaribaleStatusTypeEnum.Bad;
                    ret.Message = "读取失败，返回值为空";
                    _logger.LogError($"Device:[{_device}],ReadNode({ioArg.Address}) failed: null data value");
                    return ret;
                }

                if (!DataValue.IsGood(dataValue))
                {
                    ret.StatusType = VaribaleStatusTypeEnum.Bad;
                    ret.Message = $"读取失败: {dataValue.StatusCode}";
                    _logger.LogError($"Device:[{_device}],ReadNode({ioArg.Address}) failed: {dataValue.StatusCode}");
                    return ret;
                }

                ret.Value = dataValue.Value;
                if (ret.Value == null)
                {
                    ret.StatusType = VaribaleStatusTypeEnum.Bad;
                    ret.Message = "读取到空值";
                    _logger.LogError($"Device:[{_device}],ReadNode({ioArg.Address}) failed: null value");
                }
                else
                {
                    _logger.LogInformation($"Device:[{_device}],ReadNode({ioArg.Address}) succeeded: {ret.Value}");
                }
            }
            catch (Exception ex)
            {
                ret.StatusType = VaribaleStatusTypeEnum.Bad;
                ret.Message = $"读取失败: {ex.Message}";
                _logger.LogError(ex, $"Device:[{_device}],ReadNode({ioArg.Address}) failed with exception");
            }

            return ret;
        }

        [Method("测试方法", description: "测试方法，返回当前时间")]
        public DriverReturnValueModel Read(DriverAddressIoArgModel ioArg)
        {
            var ret = new DriverReturnValueModel { StatusType = VaribaleStatusTypeEnum.Good };

            if (IsConnected)
                ret.Value = DateTime.Now;
            else
            {
                ret.StatusType = VaribaleStatusTypeEnum.Bad;
                ret.Message = "连接失败";
            }

            return ret;
        }

        public async Task<RpcResponse> WriteAsync(string requestId, string method, DriverAddressIoArgModel ioArg)
        {
            RpcResponse rpcResponse = new() { IsSuccess = false, Description = "设备驱动内未实现写入功能" };
            await Task.CompletedTask;
            return rpcResponse;
        }

        public Dictionary<string, AddressDefinitionInfo> GetAddressDefinitions()
        {
            return UaClientAddressDefinitions.GetDefinitions();
        }
    }
}