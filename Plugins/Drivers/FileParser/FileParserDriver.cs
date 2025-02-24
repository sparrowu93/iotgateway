using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FileParser.Models;
using FileParser.Services;
using FileParser.Interfaces;
using IoTGateway.Model;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using PluginInterface;
using Plugin;

namespace FileParser;

[DriverSupported("FileParser")]
[DriverInfo("FileParser", "V1.0.0", "Copyright IoTGateway 2024-02-22")]
public class FileParserDriver : IDriver
{
    private readonly string _deviceId;
    private FileParserConfig? _config;
    private FileMonitor? _monitor;
    private DataCache? _dataCache;
    private VariableCollector? _variableCollector;

    public bool IsConnected { get; private set; }
    public ILogger _logger { get; set; }
    public string DeviceId { get; set; }
    public int Timeout { get; set; } = 3000;
    public uint MinPeriod { get; set; } = 3000;

    public FileParserDriver(ILogger logger, string deviceId)
    {
        _logger = logger;
        _deviceId = deviceId;
        DeviceId = deviceId;
    }

    public void SetConfig(FileParserConfig config)
    {
        _config = config;
    }

    public bool Connect()
    {
        try
        {
            if (_config == null)
            {
                _logger.LogError("未设置配置");
                return false;
            }

            if (string.IsNullOrEmpty(_config.ServerType) || 
                !new[] { "FTP", "SMB", "LOCAL" }.Contains(_config.ServerType.ToUpper()))
            {
                _logger.LogError($"不支持的服务器类型: {_config.ServerType}");
                return false;
            }

            _dataCache = new DataCache();
            _variableCollector = new VariableCollector(_dataCache);
            _monitor = new FileMonitor(_config, new List<IFileParser> { new XmlParser(), new CsvParser() }, _dataCache);
            _monitor.Start();

            IsConnected = true;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "连接失败");
            IsConnected = false;
            return false;
        }
    }

    public bool Close()
    {
        try
        {
            _monitor?.Stop();
            IsConnected = false;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "关闭失败");
            return false;
        }
    }

    public object? ReadNode(string address)
    {
        try
        {
            if (!IsConnected)
                return null;

            if (address == "status.connected")
                return IsConnected;

            return _variableCollector?.GetValue(address);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取节点失败");
            return null;
        }
    }

    [Method("读取", description: "文件解析器")]
    public DriverReturnValueModel Read(DriverAddressIoArgModel ioArg)
    {
        try
        {
            var value = ReadNode(ioArg.Address);
            return new DriverReturnValueModel
            {
                Value = value,
                Message = value != null ? "读取成功" : "读取失败",
                Timestamp = DateTime.Now
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取失败");
            return new DriverReturnValueModel
            {
                Value = null,
                Message = ex.Message,
                Timestamp = DateTime.Now
            };
        }
    }

    public Task<RpcResponse> WriteAsync(string deviceId, string address, DriverAddressIoArgModel ioArg)
    {
        var response = new RpcResponse
        {
            IsSuccess = false,
            Description = "FileParser驱动不支持写入操作"
        };
        return Task.FromResult(response);
    }

    public void Dispose()
    {
        _monitor?.Dispose();
    }
}
