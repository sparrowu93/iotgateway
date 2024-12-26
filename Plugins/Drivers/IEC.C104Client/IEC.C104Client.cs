using System;
using System.Collections.Generic;
using System.Threading;
using lib60870;
using lib60870.CS101;
using lib60870.CS104;
using PluginInterface;
using Microsoft.Extensions.Logging;

namespace DriverIEC60870
{
    [DriverSupported("IEC60870-C104")]
    [DriverInfo("IEC60870Client-104", "V1.0.0", "Copyright IoTGateway© 2024-03-15")]
    public class IEC60870Client : IDriver
    {
        private Connection _connection;
        private readonly object _lockObject = new object();
        private Dictionary<string, object> _latestData = new Dictionary<string, object>();

        public ILogger _logger { get; set; }
        private readonly string _device;

        #region 配置参数
        [ConfigParameter("设备Id")] public string DeviceId { get; set; }
        [ConfigParameter("IP地址")] public string IpAddress { get; set; } = "127.0.0.1";
        [ConfigParameter("端口号")] public int Port { get; set; } = 2404;
        [ConfigParameter("ASDU地址")] public int AsduAddress { get; set; } = 1;
        [ConfigParameter("超时时间(ms)")] public int Timeout { get; set; } = 5000;
        [ConfigParameter("最小周期(ms)")] public uint MinPeriod { get; set; } = 1000;
        #endregion

        public IEC60870Client(string device, ILogger logger)
        {
            _device = device;
            _logger = logger;
            _logger.LogInformation($"Device:[{_device}],Create()");
        }

        public bool IsConnected => _connection != null && _connection.IsRunning;

        public bool Connect()
        {
            try
            {
                _connection = new Connection(IpAddress, Port);
                _connection.SetASDUReceivedHandler(AsduReceivedHandler, null);
                _connection.SetConnectionHandler(ConnectionHandler, null);

                _connection.Connect();

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"连接失败: {ex.Message}");
                return false;
            }
        }
        private void ConnectionHandler(object parameter, ConnectionEvent connectionEvent)
        {
            switch (connectionEvent)
            {
                case ConnectionEvent.OPENED:
                    _logger.LogInformation("Connection opened");
                    break;
                case ConnectionEvent.CLOSED:
                    _logger.LogInformation("Connection closed");
                    break;
                case ConnectionEvent.STARTDT_CON_RECEIVED:
                    _logger.LogInformation("STARTDT CON received");
                    break;
                case ConnectionEvent.STOPDT_CON_RECEIVED:
                    _logger.LogInformation("STOPDT CON received");
                    break;
            }
        }

        private bool AsduReceivedHandler(object parameter, ASDU asdu)
        {
            _logger.LogInformation($"Received ASDU type: {asdu.TypeId}");
            // 判断来源地址, 如果不是监听地址则进行忽略
            if (asdu.Ca != AsduAddress)
            {
                _logger.LogInformation($"Ignoring ASDU with address {asdu.Ca} (expected {AsduAddress})");
                return true; // Return true to continue receiving messages
            }


            lock (_lockObject)
            {
                for (int i = 0; i < asdu.NumberOfElements; i++)
                {
                    var ioe = asdu.GetElement(i);

                    switch (asdu.TypeId)
                    {
                        case TypeID.M_SP_NA_1: // 单点信息
                            var sp = (SinglePointInformation)ioe;
                            _latestData[$"SP_{sp.ObjectAddress}"] = sp.Value;
                            break;
                        case TypeID.M_DP_NA_1: // 双点信息
                            var dp = (DoublePointInformation)ioe;
                            _latestData[$"DP_{dp.ObjectAddress}"] = dp.Value;
                            break;
                        case TypeID.M_ST_NA_1: // 步位置信息
                            var st = (StepPositionInformation)ioe;
                            _latestData[$"ST_{st.ObjectAddress}"] = st.Value;
                            break;
                        case TypeID.M_BO_NA_1: // 32位串
                            var bo = (Bitstring32)ioe;
                            _latestData[$"BO_{bo.ObjectAddress}"] = bo.Value;
                            break;
                        case TypeID.M_ME_NA_1: // 测量值，规一化值
                            var mme = (MeasuredValueNormalized)ioe;
                            _latestData[$"MME_{mme.ObjectAddress}"] = mme.NormalizedValue;
                            break;
                        case TypeID.M_ME_NB_1: // 测量值，标度化值
                            var mmes = (MeasuredValueScaled)ioe;
                            _latestData[$"MMES_{mmes.ObjectAddress}"] = mmes.ScaledValue;
                            break;
                        case TypeID.M_ME_NC_1: // 测量值，短浮点数
                            var mmef = (MeasuredValueShort)ioe;
                            _latestData[$"MMEF_{mmef.ObjectAddress}"] = mmef.Value;
                            // _logger.LogInformation($"Received ASDU type: {mmef.ObjectAddress}");
                            break;
                        case TypeID.M_IT_NA_1: // 积累量
                            var it = (IntegratedTotals)ioe;
                            _latestData[$"IT_{it.ObjectAddress}"] = it.BCR.Value;
                            break;
                        case TypeID.M_PS_NA_1: // 带时标的包状态
                            var ps = (PackedSinglePointWithSCD)ioe;
                            _latestData[$"PS_{ps.ObjectAddress}"] = ps.SCD;
                            break;
                        case TypeID.M_ME_ND_1: // 测量值，不带品质描述词的规一化值
                            var mmen = (MeasuredValueNormalizedWithoutQuality)ioe;
                            _latestData[$"MMEN_{mmen.ObjectAddress}"] = mmen.NormalizedValue;
                            break;
                        // 带时标的信息
                        case TypeID.M_SP_TB_1: // 带时标CP56Time2a的单点信息
                            var spt = (SinglePointWithCP56Time2a)ioe;
                            _latestData[$"SPT_{spt.ObjectAddress}"] = new { Value = spt.Value, Timestamp = spt.Timestamp };
                            break;
                        case TypeID.M_DP_TB_1: // 带时标CP56Time2a的双点信息
                            var dpt = (DoublePointWithCP56Time2a)ioe;
                            _latestData[$"DPT_{dpt.ObjectAddress}"] = new { Value = dpt.Value, Timestamp = dpt.Timestamp };
                            break;
                        case TypeID.M_ST_TB_1: // 带时标CP56Time2a的步位置信息
                            var stt = (StepPositionWithCP56Time2a)ioe;
                            _latestData[$"STT_{stt.ObjectAddress}"] = new { Value = stt.Value, Timestamp = stt.Timestamp };
                            break;
                        case TypeID.M_ME_TD_1: // 带时标CP56Time2a的测量值，规一化值
                            var mmet = (MeasuredValueNormalizedWithCP56Time2a)ioe;
                            _latestData[$"MMET_{mmet.ObjectAddress}"] = new { Value = mmet.NormalizedValue, Timestamp = mmet.Timestamp };
                            break;
                        case TypeID.M_ME_TE_1: // 带时标CP56Time2a的测量值，标度化值
                            var mmest = (MeasuredValueScaledWithCP56Time2a)ioe;
                            _latestData[$"MMEST_{mmest.ObjectAddress}"] = new { Value = mmest.ScaledValue, Timestamp = mmest.Timestamp };
                            break;
                        case TypeID.M_ME_TF_1: // 带时标CP56Time2a的测量值，短浮点数
                            var mmeft = (MeasuredValueShortWithCP56Time2a)ioe;
                            _latestData[$"MMEFT_{mmeft.ObjectAddress}"] = new { Value = mmeft.Value, Timestamp = mmeft.Timestamp };
                            break;
                        case TypeID.M_IT_TB_1: // 带时标CP56Time2a的积累量
                            var itt = (IntegratedTotalsWithCP56Time2a)ioe;
                            _latestData[$"ITT_{itt.ObjectAddress}"] = new { Value = itt.BCR.Value, Timestamp = itt.Timestamp };
                            break;
                        default:
                            _logger.LogWarning($"Unhandled ASDU type: {asdu.TypeId}");
                            break;
                    }
                }
            }

            Console.WriteLine($"Loop _latestData");

            foreach (KeyValuePair<string, object> kvp in _latestData)
            {
                Console.WriteLine($"Name: {kvp.Key}, Age: {kvp.Value}");
            }

            return true;
        }

        public bool Close()
        {
            if (_connection != null)
            {
                _connection.Close();
                _connection = null;
            }
            return true;
        }

        public void Dispose()
        {
            Close();
        }


        [Method("读取通用数据", description: "使用指定前缀 <prefix>_<Address> 格式")]
        public DriverReturnValueModel Read(DriverAddressIoArgModel ioarg)
        {
            return ReadGeneric(ioarg, "");
        }

        [Method("读取单点信息", description: "读取最新接收到的单点信息 (M_SP_NA_1)")]
        public DriverReturnValueModel ReadSinglePoint(DriverAddressIoArgModel ioarg)
        {
            return ReadGeneric(ioarg, "SP_");
        }

        [Method("读取带时标的单点信息", description: "读取最新接收到的带时标的单点信息 (M_SP_TA_1, M_SP_TB_1)")]
        public DriverReturnValueModel ReadSinglePointWithTimestamp(DriverAddressIoArgModel ioarg)
        {
            return ReadGeneric(ioarg, "SPT_");
        }

        [Method("读取双点信息", description: "读取最新接收到的双点信息 (M_DP_NA_1)")]
        public DriverReturnValueModel ReadDoublePoint(DriverAddressIoArgModel ioarg)
        {
            return ReadGeneric(ioarg, "DP_");
        }

        [Method("读取带时标的双点信息", description: "读取最新接收到的带时标的双点信息 (M_DP_TB_1)")]
        public DriverReturnValueModel ReadDoublePointWithTimestamp(DriverAddressIoArgModel ioarg)
        {
            return ReadGeneric(ioarg, "DPT_");
        }

        [Method("读取步位置信息", description: "读取最新接收到的步位置信息 (M_ST_NA_1)")]
        public DriverReturnValueModel ReadStepPosition(DriverAddressIoArgModel ioarg)
        {
            return ReadGeneric(ioarg, "ST_");
        }

        [Method("读取带时标的步位置信息", description: "读取最新接收到的带时标的步位置信息 (M_ST_TA_1, M_ST_TB_1)")]
        public DriverReturnValueModel ReadStepPositionWithTimestamp(DriverAddressIoArgModel ioarg)
        {
            return ReadGeneric(ioarg, "STT_");
        }

        [Method("读取32位比特串", description: "读取最新接收到的32位比特串 (M_BO_NA_1)")]
        public DriverReturnValueModel ReadBitstring32(DriverAddressIoArgModel ioarg)
        {
            return ReadGeneric(ioarg, "BO_");
        }

        [Method("读取带时标的32位比特串", description: "读取最新接收到的带时标的32位比特串 (M_BO_TA_1, M_BO_TB_1)")]
        public DriverReturnValueModel ReadBitstring32WithTimestamp(DriverAddressIoArgModel ioarg)
        {
            return ReadGeneric(ioarg, "BOT_");
        }

        [Method("读取归一化值", description: "读取最新接收到的归一化值 (M_ME_NA_1, M_ME_ND_1)")]
        public DriverReturnValueModel ReadNormalizedValue(DriverAddressIoArgModel ioarg)
        {
            return ReadGeneric(ioarg, "MME_");
        }

        [Method("读取带时标的归一化值", description: "读取最新接收到的带时标的归一化值 (M_ME_TA_1, M_ME_TD_1)")]
        public DriverReturnValueModel ReadNormalizedValueWithTimestamp(DriverAddressIoArgModel ioarg)
        {
            return ReadGeneric(ioarg, "MMET_");
        }

        [Method("读取标度化值", description: "读取最新接收到的标度化值 (M_ME_NB_1)")]
        public DriverReturnValueModel ReadScaledValue(DriverAddressIoArgModel ioarg)
        {
            return ReadGeneric(ioarg, "MMES_");
        }

        [Method("读取带时标的标度化值", description: "读取最新接收到的带时标的标度化值 (M_ME_TB_1, M_ME_TE_1)")]
        public DriverReturnValueModel ReadScaledValueWithTimestamp(DriverAddressIoArgModel ioarg)
        {
            return ReadGeneric(ioarg, "MMEST_");
        }

        [Method("读取短浮点数", description: "读取最新接收到的短浮点数 (M_ME_NC_1)")]
        public DriverReturnValueModel ReadFloatValue(DriverAddressIoArgModel ioarg)
        {
            return ReadGeneric(ioarg, "MMEF_");
        }

        [Method("读取带时标的短浮点数", description: "读取最新接收到的带时标的短浮点数 (M_ME_TC_1, M_ME_TF_1)")]
        public DriverReturnValueModel ReadFloatValueWithTimestamp(DriverAddressIoArgModel ioarg)
        {
            return ReadGeneric(ioarg, "MMEFT_");
        }

        [Method("读取累计量", description: "读取最新接收到的累计量 (M_IT_NA_1)")]
        public DriverReturnValueModel ReadIntegratedTotals(DriverAddressIoArgModel ioarg)
        {
            return ReadGeneric(ioarg, "IT_");
        }

        [Method("读取带时标的累计量", description: "读取最新接收到的带时标的累计量 (M_IT_TA_1, M_IT_TB_1)")]
        public DriverReturnValueModel ReadIntegratedTotalsWithTimestamp(DriverAddressIoArgModel ioarg)
        {
            return ReadGeneric(ioarg, "ITT_");
        }

        [Method("读取继电保护设备事件", description: "读取最新接收到的继电保护设备事件 (M_EP_TA_1, M_EP_TD_1)")]
        public DriverReturnValueModel ReadProtectionEvent(DriverAddressIoArgModel ioarg)
        {
            return ReadGeneric(ioarg, "EP_");
        }

        [Method("读取继电保护设备成组启动事件", description: "读取最新接收到的继电保护设备成组启动事件 (M_EP_TB_1, M_EP_TE_1)")]
        public DriverReturnValueModel ReadProtectionStartEvent(DriverAddressIoArgModel ioarg)
        {
            return ReadGeneric(ioarg, "EPS_");
        }

        [Method("读取继电保护设备成组输出电路信息", description: "读取最新接收到的继电保护设备成组输出电路信息 (M_EP_TC_1, M_EP_TF_1)")]
        public DriverReturnValueModel ReadProtectionOutputCircuitInfo(DriverAddressIoArgModel ioarg)
        {
            return ReadGeneric(ioarg, "EPO_");
        }

        [Method("读取带变位检出的成组单点信息", description: "读取最新接收到的带变位检出的成组单点信息 (M_PS_NA_1)")]
        public DriverReturnValueModel ReadPackedSinglePointWithSCD(DriverAddressIoArgModel ioarg)
        {
            return ReadGeneric(ioarg, "PS_");
        }

        private DriverReturnValueModel ReadGeneric(DriverAddressIoArgModel ioarg, string prefix)
        {
            var ret = new DriverReturnValueModel { StatusType = VaribaleStatusTypeEnum.Good };

            if (!IsConnected)
            {
                ret.StatusType = VaribaleStatusTypeEnum.Bad;
                ret.Message = "未连接";
                return ret;
            }

            try
            {
                lock (_lockObject)
                {
                    string fullAddress = $"{prefix}{ioarg.Address}";
                    if (_latestData.TryGetValue(fullAddress, out object value))
                    {
                        ret.Value = value;
                    }
                    else
                    {
                        ret.StatusType = VaribaleStatusTypeEnum.Bad;
                        ret.Message = $"未找到指定地址的数据 {fullAddress}";
                    }
                }
            }
            catch (Exception ex)
            {
                ret.StatusType = VaribaleStatusTypeEnum.Bad;
                ret.Message = $"读取失败: {ex.Message}";
            }

            return ret;
        }

        public async Task<RpcResponse> WriteAsync(string requestId, string method, DriverAddressIoArgModel ioarg)
        {
            // 实现写操作，如果需要的话
            return new RpcResponse { IsSuccess = false, Description = "此驱动暂不支持写入操作" };
        }
    }
}