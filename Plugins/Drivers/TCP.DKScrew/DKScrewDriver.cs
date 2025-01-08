using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using TCP.DKScrew.Models;
using TCP.DKScrew.Communication;
using PluginInterface;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace TCP.DKScrew
{
    [DriverSupported("DKScrewDevice")]
    [DriverInfo("DKScrewDriver", "V1.0.0", "Copyright IoTGateway 2024-01-06")]
    public class DKScrewDriver : IDriver, IDisposable, IAddressDefinitionProvider
    {
        private readonly TcpClientWrapper _client;
        private bool _isConnected;
        private readonly object _lock = new object();
        private RunStatus? _lastStatus;
        private TighteningResult? _lastTighteningResult;
        private CurveData? _lastCurveData;
        private int _currentPset;
        public ILogger _logger { get; set; }

        public event EventHandler<RunStatus>? OnStatusChanged;
        public event EventHandler<TighteningResult>? OnTighteningComplete;
        public event EventHandler<CurveData>? OnCurveDataReceived;

        #region Configuration Parameters
        [ConfigParameter("设备Id")]
        public string DeviceId { get; set; }

        [ConfigParameter("主机地址")]
        public string Host { get; set; } = "127.0.0.1";

        [ConfigParameter("端口")]
        public int Port { get; set; } = 4196;

        [ConfigParameter("超时时间ms")]
        public int Timeout { get; set; } = 3000;

        [ConfigParameter("最小通讯周期ms")]
        public uint MinPeriod { get; set; } = 3000;
        #endregion

        public bool IsConnected => _isConnected;

        public DKScrewDriver(string device, ILogger logger)
        {
            DeviceId = device;
            _logger = logger;
            _client = new TcpClientWrapper(Host, Port);
            _logger.LogInformation($"Device:[{device}],Create()");
        }

        public bool Connect()
        {
            try
            {
                ConnectAsync().GetAwaiter().GetResult();
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to connect to DKScrew device");
                return false;
            }
        }

        public bool Close()
        {
            try
            {
                DisconnectAsync().GetAwaiter().GetResult();
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to disconnect from DKScrew device");
                return false;
            }
        }

        /// <summary>
        /// 读取电批设备的变量值
        /// </summary>
        /// <remarks>
        /// 支持以下变量类型：
        /// 
        /// 1. 系统状态变量：
        ///    - IsConnected: 设备连接状态
        ///    - IsReady: 设备就绪状态
        ///    - IsRunning: 设备运行状态
        ///    - IsOK: 上一次拧紧结果是否合格
        ///    - IsNG: 上一次拧紧结果是否不合格
        ///    - HasSystemError: 是否存在系统错误
        ///    - SystemErrorId: 系统错误代码
        ///    - RunStatus: 当前运行状态
        ///    
        /// 2. 拧紧结果变量：
        ///    - FinalTorque: 最终扭矩值 (N·m)
        ///    - MonitoringAngle: 监控角度 (度)
        ///    - FinalTime: 拧紧时间 (秒)
        ///    - FinalAngle: 最终角度 (度)
        ///    - ResultStatus: 拧紧结果状态
        ///    - NGCode: 不合格代码
        ///    - LastTighteningResult: 完整的拧紧结果JSON数据
        ///    示例：
        ///    {
        ///      "finalTorque": 12.5,
        ///      "monitoringAngle": 180.0,
        ///      "finalTime": 2.5,
        ///      "finalAngle": 185.2,
        ///      "resultStatus": 1,
        ///      "ngCode": 0
        ///    }
        ///    
        /// 3. 曲线数据变量：
        ///    - CurrentTorque: 当前扭矩值 (N·m)
        ///    - CurrentAngle: 当前角度 (度)
        ///    - IsCurveFinished: 曲线是否完成
        ///    - IsCurveStart: 曲线是否开始
        ///    - LastCurveData: 完整的曲线数据JSON
        ///    示例：
        ///    {
        ///      "curvePoints": [
        ///        {"time": 0.0, "torque": 0.0, "angle": 0.0},
        ///        {"time": 0.1, "torque": 2.5, "angle": 45.0},
        ///        {"time": 0.2, "torque": 5.0, "angle": 90.0}
        ///      ],
        ///      "isCurveFinished": true,
        ///      "isCurveStart": false
        ///    }
        ///    
        /// 4. 控制参数变量：
        ///    - CurrentPset: 当前程序号
        /// </remarks>
        /// <param name="ioArg">包含地址信息的参数模型，地址为变量名称</param>
        /// <returns>包含解析结果的驱动返回值模型</returns>
        public DriverReturnValueModel Read(DriverAddressIoArgModel ioArg)
        {
            if (!_isConnected)
            {
                return new DriverReturnValueModel 
                { 
                    StatusType = VaribaleStatusTypeEnum.Bad,
                    Message = "Not connected to DKScrew device" 
                };
            }

            try
            {
                var variableName = ioArg.Address;
                object? value = null;

                switch (variableName)
                {
                    // 系统状态变量
                    case DeviceVariables.IsConnected:
                        value = _isConnected;
                        break;
                    case DeviceVariables.IsReady:
                        value = _lastStatus?.IsReady ?? false;
                        break;
                    case DeviceVariables.IsRunning:
                        value = _lastStatus?.IsRunning ?? false;
                        break;
                    case DeviceVariables.IsOK:
                        value = _lastStatus?.IsOK ?? false;
                        break;
                    case DeviceVariables.IsNG:
                        value = _lastStatus?.IsNG ?? false;
                        break;
                    case DeviceVariables.HasSystemError:
                        value = _lastStatus?.HasSystemError ?? false;
                        break;
                    case DeviceVariables.SystemErrorId:
                        value = _lastStatus?.SystemErrorId ?? 0;
                        break;
                    case DeviceVariables.RunStatus:
                        value = _lastStatus?.IsRunning ?? false;
                        break;

                    // 拧紧结果变量
                    case DeviceVariables.FinalTorque:
                        value = _lastTighteningResult?.FinalTorque ?? 0.0;
                        break;
                    case DeviceVariables.MonitoringAngle:
                        value = _lastTighteningResult?.MonitoringAngle ?? 0.0;
                        break;
                    case DeviceVariables.FinalTime:
                        value = _lastTighteningResult?.FinalTime ?? 0.0;
                        break;
                    case DeviceVariables.FinalAngle:
                        value = _lastTighteningResult?.FinalAngle ?? 0.0;
                        break;
                    case DeviceVariables.ResultStatus:
                        value = _lastTighteningResult?.ResultStatus ?? 0;
                        break;
                    case DeviceVariables.NGCode:
                        value = _lastTighteningResult?.NGCode ?? 0;
                        break;
                    case DeviceVariables.LastTighteningResult:
                        value = _lastTighteningResult != null ? JsonSerializer.Serialize(_lastTighteningResult) : null;
                        break;

                    // 曲线数据变量
                    case DeviceVariables.CurrentTorque:
                        value = _lastCurveData?.CurrentTorque ?? 0.0;
                        break;
                    case DeviceVariables.CurrentAngle:
                        value = _lastCurveData?.CurrentAngle ?? 0.0;
                        break;
                    case DeviceVariables.IsCurveFinished:
                        value = _lastCurveData?.IsCurveFinished ?? false;
                        break;
                    case DeviceVariables.IsCurveStart:
                        value = _lastCurveData?.IsCurveStart ?? false;
                        break;
                    case DeviceVariables.LastCurveData:
                        value = _lastCurveData != null ? JsonSerializer.Serialize(_lastCurveData) : null;
                        break;

                    // 控制参数变量
                    case DeviceVariables.CurrentPset:
                        value = _currentPset;
                        break;

                    default:
                        return new DriverReturnValueModel
                        {
                            StatusType = VaribaleStatusTypeEnum.Bad,
                            Message = $"Unknown variable: {variableName}"
                        };
                }

                return new DriverReturnValueModel
                {
                    StatusType = VaribaleStatusTypeEnum.Good,
                    Value = value
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Error reading variable {ioArg.Address}");
                return new DriverReturnValueModel
                {
                    StatusType = VaribaleStatusTypeEnum.Bad,
                    Message = $"Error reading from device: {ex.Message}"
                };
            }
        }

        public async Task<RpcResponse> WriteAsync(string RequestId, string Method, DriverAddressIoArgModel ioArg)
        {
            if (!_isConnected)
            {
                return new RpcResponse { IsSuccess = false, Description = "Not connected to DKScrew device" };
            }

            try
            {
                switch (Method.ToLower())
                {
                    case "start":
                        await StartMotorAsync();
                        break;
                    case "stop":
                        await StopMotorAsync();
                        break;
                    case "loosen":
                        await LoosenAsync();
                        break;
                    default:
                        return new RpcResponse { IsSuccess = false, Description = $"Unknown method: {Method}" };
                }

                return new RpcResponse { IsSuccess = true };
            }
            catch (Exception ex)
            {
                return new RpcResponse
                {
                    IsSuccess = false,
                    Description = $"Failed to execute command: {ex.Message}"
                };
            }
        }

        public async Task ConnectAsync()
        {
            if (_isConnected)
                return;

            try
            {
                await _client.ConnectAsync();
                
                // Send connection request
                var response = await SendCommandAsync(PacketBuilder.BuildConnectionRequest());
                if (response.IsError)
                    throw new Exception($"Connection failed: Error {response.ErrorCode}");

                if (response.ResponseType != Protocol.AckResponse)
                    throw new Exception("Invalid response type for connection request");

                _isConnected = true;
            }
            catch (Exception)
            {
                await DisconnectAsync();
                throw;
            }
        }

        public async Task DisconnectAsync()
        {
            if (!_isConnected)
                return;

            try
            {
                // Send disconnect request
                await SendCommandAsync(PacketBuilder.BuildDisconnectRequest());
            }
            finally
            {
                await _client.DisconnectAsync();
                _isConnected = false;
            }
        }

        private async Task<PacketParser.ParsedPacket> SendCommandAsync(byte[] command)
        {
            CheckConnection();
            var responseData = await _client.SendAndReceiveAsync(command);
            return PacketParser.Parse(responseData);
        }

        private void CheckConnection()
        {
            if (!_isConnected)
                throw new InvalidOperationException("Not connected to the device");
        }

        // Motor Control Methods
        public async Task StartMotorAsync()
        {
            var response = await SendCommandAsync(PacketBuilder.BuildMotorControlRequest(Protocol.MotorCommand.Start));
            if (response.IsError)
                throw new Exception($"Start motor failed: Error {response.ErrorCode}");
        }

        public async Task StopMotorAsync()
        {
            var response = await SendCommandAsync(PacketBuilder.BuildMotorControlRequest(Protocol.MotorCommand.EmergencyStop));
            if (response.IsError)
                throw new Exception($"Stop motor failed: Error {response.ErrorCode}");
        }

        public async Task LoosenAsync()
        {
            var response = await SendCommandAsync(PacketBuilder.BuildMotorControlRequest(Protocol.MotorCommand.Loosen));
            if (response.IsError)
                throw new Exception($"Loosen operation failed: Error {response.ErrorCode}");
        }

        // Pset Methods
        public async Task SelectPsetAsync(int psetNumber)
        {
            if (psetNumber < 1 || psetNumber > 8)
                throw new ArgumentException("Pset number must be between 1 and 8");

            var response = await SendCommandAsync(PacketBuilder.BuildPsetSelectRequest(psetNumber));
            if (response.IsError)
                throw new Exception($"Pset selection failed: Error {response.ErrorCode}");
        }

        public async Task<PsetParameter> GetPsetDataAsync(int? psetNumber = null)
        {
            var response = await SendCommandAsync(PacketBuilder.BuildPsetDataRequest(psetNumber));
            if (response.IsError)
                throw new Exception($"Get Pset data failed: Error {response.ErrorCode}");

            // TODO: Implement Pset data parsing
            return new PsetParameter();
        }

        // Status and Result Methods
        public async Task<RunStatus> GetRunStatusAsync()
        {
            var response = await SendCommandAsync(PacketBuilder.BuildRunStatusRequest());
            if (response.IsError)
                throw new Exception($"Get run status failed: Error {response.ErrorCode}");

            var status = PacketParser.ParseRunStatus(response);
            OnStatusChanged?.Invoke(this, status);
            return status;
        }

        public async Task<TighteningResult> GetTighteningResultAsync()
        {
            var response = await SendCommandAsync(PacketBuilder.BuildTighteningResultRequest());
            if (response.IsError)
                throw new Exception($"Get tightening result failed: Error {response.ErrorCode}");

            var result = PacketParser.ParseTighteningResult(response);
            OnTighteningComplete?.Invoke(this, result);
            return result;
        }

        public async Task<CurveData> GetCurveDataAsync()
        {
            var response = await SendCommandAsync(PacketBuilder.BuildCurveDataRequest());
            if (response.IsError)
                throw new Exception($"Get curve data failed: Error {response.ErrorCode}");

            var curveData = PacketParser.ParseCurveData(response);
            OnCurveDataReceived?.Invoke(this, curveData);
            return curveData;
        }

        public void Dispose()
        {
            _client?.Dispose();
        }

        #region IAddressDefinitionProvider Implementation
        public Dictionary<string, AddressDefinitionInfo> GetAddressDefinitions()
        {
            return DKScrewAddressDefinitions.GetDefinitions();
        }
        #endregion
    }
}
