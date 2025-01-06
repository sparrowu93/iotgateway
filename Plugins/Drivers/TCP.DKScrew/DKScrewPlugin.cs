using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.Logging;
using PluginInterface;
using TCP.DKScrew.Models;
using Newtonsoft.Json;

namespace TCP.DKScrew
{
    [DriverSupported("DKScrew")]
    [DriverInfo("DKScrew", "V1.0.0", "Copyright IoTGateway.net")]
    public class DKScrewPlugin : IDriver, IDisposable
    {
        private DKScrewDriver? _driver;
        private DKScrewDeviceConfig? _config;
        private readonly Dictionary<string, object> _variableValues;
        private CancellationTokenSource? _collectionCts;
        private Task? _dataCollectionTask;
        private Task? _statusUpdateTask;
        private readonly object _lock = new object();
        private bool _disposed = false;

        public string DeviceId { get; set; } = string.Empty;
        public bool IsConnected { get; private set; }
        public int Timeout => _config?.Timeout ?? 3000;
        public uint MinPeriod => 100;
        public ILogger _logger { get; set; }

        public DKScrewPlugin(ILogger logger)
        {
            _logger = logger;
            _variableValues = new Dictionary<string, object>();
        }

        public async Task<bool> ConnectAsync()
        {
            try
            {
                if (_config == null)
                {
                    _logger.LogError("Configuration is not set");
                    return false;
                }

                _driver = new DKScrewDriver(_config.IpAddress, _config.Port);
                await _driver.ConnectAsync();
                
                // 注册事件处理
                _driver.OnStatusChanged += OnStatusChanged;
                _driver.OnTighteningComplete += OnTighteningComplete;
                _driver.OnCurveDataReceived += OnCurveDataReceived;

                // 启动数据采集任务
                StartDataCollection();

                IsConnected = true;
                UpdateVariable(DeviceVariables.IsConnected, true);
                _logger.LogInformation($"Connected to DKScrew device at {_config.IpAddress}:{_config.Port}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to connect to DKScrew device: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DisconnectAsync()
        {
            try
            {
                StopDataCollection();

                if (_driver != null)
                {
                    await _driver.DisconnectAsync();
                    _driver.Dispose();
                    _driver = null;
                }

                IsConnected = false;
                UpdateVariable(DeviceVariables.IsConnected, false);
                _logger.LogInformation("Disconnected from DKScrew device");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error disconnecting from DKScrew device: {ex.Message}");
                return false;
            }
        }

        public void Configure(string configuration)
        {
            _config = JsonConvert.DeserializeObject<DKScrewDeviceConfig>(configuration);
        }

        public async Task<object> ReadNode(string node)
        {
            var result = new DriverReturnValueModel { StatusType = VaribaleStatusTypeEnum.Good };

            try
            {
                if (!IsConnected)
                {
                    result.StatusType = VaribaleStatusTypeEnum.Bad;
                    return result;
                }

                lock (_lock)
                {
                    if (_variableValues.TryGetValue(node, out object? value))
                    {
                        result.Value = value;
                    }
                    else
                    {
                        result.StatusType = VaribaleStatusTypeEnum.Bad;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error reading variable {node}: {ex.Message}");
                result.StatusType = VaribaleStatusTypeEnum.Bad;
            }

            return result;
        }

        public async Task<bool> WriteNode(string node, object value)
        {
            try
            {
                if (!IsConnected || _driver == null)
                    return false;

                switch (node)
                {
                    case DeviceVariables.StartMotor:
                        if (Convert.ToBoolean(value))
                            await _driver.StartMotorAsync();
                        break;

                    case DeviceVariables.StopMotor:
                        if (Convert.ToBoolean(value))
                            await _driver.StopMotorAsync();
                        break;

                    case DeviceVariables.LoosenMotor:
                        if (Convert.ToBoolean(value))
                            await _driver.LoosenAsync();
                        break;

                    case DeviceVariables.SelectPset:
                        int psetNumber = Convert.ToInt32(value);
                        await _driver.SelectPsetAsync(psetNumber);
                        UpdateVariable(DeviceVariables.CurrentPset, psetNumber);
                        break;

                    default:
                        return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error writing to variable {node}: {ex.Message}");
                return false;
            }
        }

        private void StartDataCollection()
        {
            _collectionCts = new CancellationTokenSource();

            // 状态更新任务
            _statusUpdateTask = Task.Run(async () =>
            {
                while (!_collectionCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        if (IsConnected && _driver != null)
                        {
                            await _driver.GetRunStatusAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error updating status: {ex.Message}");
                    }

                    if (_config != null)
                    {
                        await Task.Delay(_config.StatusUpdateInterval, _collectionCts.Token);
                    }
                }
            }, _collectionCts.Token);

            // 曲线数据采集任务
            if (_config?.EnableCurveData == true)
            {
                _dataCollectionTask = Task.Run(async () =>
                {
                    while (!_collectionCts.Token.IsCancellationRequested)
                    {
                        try
                        {
                            if (IsConnected && _driver != null)
                            {
                                await _driver.GetCurveDataAsync();
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Error collecting curve data: {ex.Message}");
                        }

                        if (_config != null)
                        {
                            await Task.Delay(_config.CurveDataInterval, _collectionCts.Token);
                        }
                    }
                }, _collectionCts.Token);
            }
        }

        private void StopDataCollection()
        {
            if (_collectionCts != null)
            {
                _collectionCts.Cancel();
                try
                {
                    if (_statusUpdateTask != null && _dataCollectionTask != null)
                    {
                        Task.WaitAll(new[] { _statusUpdateTask, _dataCollectionTask }, TimeSpan.FromSeconds(5));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error stopping data collection tasks: {ex.Message}");
                }
                finally
                {
                    _collectionCts = null;
                    _statusUpdateTask = null;
                    _dataCollectionTask = null;
                }
            }
        }

        private void OnStatusChanged(object? sender, RunStatus status)
        {
            UpdateVariable(DeviceVariables.RunStatus, status);
        }

        private void OnTighteningComplete(object? sender, TighteningResult result)
        {
            UpdateVariable(DeviceVariables.LastTighteningResult, result);
        }

        private void OnCurveDataReceived(object? sender, CurveData data)
        {
            UpdateVariable(DeviceVariables.LastCurveData, data);
        }

        private void UpdateVariable(string name, object value)
        {
            lock (_lock)
            {
                _variableValues[name] = value;
            }
        }

        #region IDriver Implementation

        public bool Connect() => Task.Run(async () => await ConnectAsync()).Result;

        public bool Close() => Task.Run(async () => await DisconnectAsync()).Result;

        public DriverReturnValueModel Read(DriverAddressIoArgModel ioArg)
        {
            var result = Task.Run(async () => await ReadNode(ioArg.Address)).Result;
            return result as DriverReturnValueModel ?? new DriverReturnValueModel { StatusType = VaribaleStatusTypeEnum.Bad };
        }

        public async Task<RpcResponse> WriteAsync(string requestId, string method, DriverAddressIoArgModel ioArg)
        {
            var success = await WriteNode(ioArg.Address, ioArg.Value);
            return new RpcResponse { IsSuccess = success };
        }

        #endregion

        #region IDisposable Implementation

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    StopDataCollection();
                    if (_driver != null)
                    {
                        Task.Run(async () => await DisconnectAsync()).Wait();
                        _driver.Dispose();
                        _driver = null;
                    }
                    _collectionCts?.Dispose();
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
