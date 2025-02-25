﻿using PluginInterface;
using System.Reflection;
using System.Text;
using IoTGateway.DataAccess;
using IoTGateway.Model;
using DynamicExpresso;
using MQTTnet.Server;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;
using MQTTnet;

namespace Plugin
{
    public class DeviceThread : IDisposable
    {
        private readonly MqttServer _mqttServer;
        private readonly ILogger _logger;
        public readonly Device Device;
        public readonly IDriver Driver;
        private readonly string _projectId;
        private readonly MessageService _messageService;
        private Interpreter? _interpreter;
        internal List<MethodInfo>? Methods { get; set; }
        private Task? _task;
        private readonly DateTime _tsStartDt = new(1970, 1, 1);
        private readonly CancellationTokenSource _tokenSource = new CancellationTokenSource();
        private ManualResetEvent resetEvent = new(true);

        public DeviceThread(Device device, IDriver driver, string projectId, MessageService messageService,
            MqttServer mqttServer, ILogger logger)
        {
            _messageService = messageService;
            _messageService.OnExcRpc += MyMqttClient_OnExcRpc;
            Device = device;
            Driver = driver;
            _projectId = projectId;
            _interpreter = new Interpreter();
            _logger = logger;
            _mqttServer = mqttServer;
            Methods = Driver.GetType().GetMethods().Where(x => x.GetCustomAttribute(typeof(MethodAttribute)) != null)
                .ToList();
            if (Device.AutoStart)
            {
                _logger.LogInformation($"线程已启动:{Device.DeviceName}");

                if (Device.DeviceVariables != null)
                {
                    foreach (var item in Device.DeviceVariables)
                    {
                        item.StatusType = VaribaleStatusTypeEnum.Bad;
                        if (string.IsNullOrWhiteSpace(item.Alias))
                            item.Alias = string.Empty;
                    }
                }
                CreateThread().Wait();
            }
        }

        public async Task CreateThread()
        {
            _task = await Task.Factory.StartNew(async () =>
            {
                await Task.Delay(5000);
                //上传客户端属性
                foreach (var deviceVariables in Device.DeviceVariables!.GroupBy(x => x.Alias))
                {
                    _messageService.UploadAttributeAsync(string.IsNullOrWhiteSpace(deviceVariables.Key)
                            ? Device.DeviceName
                    : deviceVariables.Key,
                        Device.DeviceConfigs.Where(x => x.DataSide == DataSide.ClientSide || x.DataSide == DataSide.AnySide)
                            .ToDictionary(x => x.DeviceConfigName, x => x.Value));
                }

                while (true)
                {
                    if (_tokenSource.IsCancellationRequested)
                    {
                        _logger.LogInformation($"停止线程:{Device.DeviceName}");
                        return;
                    }

                    resetEvent.WaitOne();
                    try
                    {
                        if (Driver.IsConnected)
                        {
                            foreach (var deviceVariables in Device.DeviceVariables.Where(x => x.ProtectType != ProtectTypeEnum.WriteOnly).GroupBy(x => x.Alias))
                            {
                                string deviceName = string.IsNullOrWhiteSpace(deviceVariables.Key)
                                    ? Device.DeviceName
                                    : deviceVariables.Key;

                                Dictionary<string, List<PayLoad>> sendModel = new()
                                            { { deviceName, new() } };

                                if (deviceVariables.Any())
                                {
                                    var payLoadTrigger = new PayLoad() { Values = new() };

                                    bool canPub = false;
                                    var triggerVariables = deviceVariables.Where(x => x.IsTrigger).ToList();
                                    ReadVariables(ref triggerVariables, ref payLoadTrigger, _mqttServer);

                                    var triggerValues = triggerVariables.ToDictionary(x => x.Name, x => x.CookedValue);


                                    var payLoadUnTrigger = new PayLoad() { Values = new() };
                                    //有需要上传 或者全部是非触发
                                    if (triggerValues.Values.Any(x => x is true) || !triggerVariables.Any())
                                    {
                                        var variables = deviceVariables.Where(x => !triggerVariables.Select(y => y.ID).Contains(x.ID)).ToList();
                                        ReadVariables(ref variables, ref payLoadUnTrigger, _mqttServer);
                                        canPub = true;
                                    }


                                    if (canPub)
                                    {
                                        var payLoad = new PayLoad()
                                        {
                                            Values = deviceVariables
                                                .Where(x => x.StatusType == VaribaleStatusTypeEnum.Good && x.IsUpload)
                                                .ToDictionary(kv => kv.Name, kv => kv.CookedValue),
                                            DeviceStatus = payLoadTrigger.DeviceStatus
                                        };
                                        payLoad.TS = (long)(DateTime.UtcNow - _tsStartDt).TotalMilliseconds;
                                        payLoad.DeviceStatus = DeviceStatusTypeEnum.Good;
                                        sendModel[deviceName] = new List<PayLoad> { payLoad };
                                        _messageService
                                            .PublishTelemetryAsync(deviceName,
                                                Device, sendModel).Wait();
                                    }

                                    if (deviceVariables.Any(x => x.StatusType == VaribaleStatusTypeEnum.Bad))
                                        _messageService?.DeviceDisconnected(deviceName, Device);
                                }

                            }

                            //全部读取异常且连接正常就断开
                            if (Device.DeviceVariables
                                    .All(x => x.StatusType != VaribaleStatusTypeEnum.Good) && Driver.IsConnected)
                            {
                                Driver.Close();
                                Driver.Dispose();
                            }
                        }
                        else
                        {
                            foreach (var deviceVariables in Device.DeviceVariables!.GroupBy(x => x.Alias))
                            {
                                string deviceName = string.IsNullOrWhiteSpace(deviceVariables.Key)
                                    ? Device.DeviceName
                                    : deviceVariables.Key;

                                _messageService?.DeviceDisconnected(deviceName, Device);
                            }

                            if (Driver.Connect())
                            {
                                foreach (var deviceVariables in Device.DeviceVariables!.GroupBy(x => x.Alias))
                                {
                                    string deviceName = string.IsNullOrWhiteSpace(deviceVariables.Key)
                                        ? Device.DeviceName
                                        : deviceVariables.Key;

                                    _messageService?.DeviceConnected(deviceName, Device);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"线程循环异常,{Device.DeviceName}");
                    }


                    await Task.Delay(Device.DeviceVariables!.Any() ? (int)Driver.MinPeriod : 10000);
                }
            }, TaskCreationOptions.LongRunning);
        }

        private void ReadVariables(ref List<DeviceVariable> variables, ref PayLoad payLoad, MqttServer mqttServer)
        {
            if (!variables.Any())
                return;
            foreach (var item in variables.OrderBy(x => x.Index))
            {
                var ret = new DriverReturnValueModel();
                var ioarg = new DriverAddressIoArgModel
                {
                    ID = item.ID,
                    Address = item.DeviceAddress,
                    ValueType = item.DataType,
                    EndianType = item.EndianType
                };
                var method = Methods.Where(x => x.Name == item.Method).FirstOrDefault();
                if (method == null)
                    ret.StatusType = VaribaleStatusTypeEnum.MethodError;
                else
                    ret = (DriverReturnValueModel)method.Invoke(Driver,
                        new object[] { ioarg })!;

                ret.Timestamp = DateTime.Now;
                item.EnqueueVariable(ret.Value);
                if (ret.StatusType == VaribaleStatusTypeEnum.Good &&
                    !string.IsNullOrWhiteSpace(item.Expressions?.Trim()))
                {
                    var expressionText = DealMysqlStr(item.Expressions)
                        .Replace("raw",
                            item.Values[0] is bool
                                ? $"Convert.ToBoolean(\"{item.Values[0]}\")"
                                : item.Values[0]?.ToString())
                        .Replace("$ppv",
                            item.Values[2] is bool
                                ? $"Convert.ToBoolean(\"{item.Values[2]}\")"
                                : item.Values[2]?.ToString())
                        .Replace("$pv",
                            item.Values[1] is bool
                                ? $"Convert.ToBoolean(\"{item.Values[1]}\")"
                                : item.Values[1]?.ToString());
                    try
                    {
                        ret.CookedValue = _interpreter.Eval(expressionText);
                    }
                    catch (Exception)
                    {
                        ret.Message = $"表达式错误：{expressionText}";
                        ret.StatusType = VaribaleStatusTypeEnum.ExpressionError;
                    }
                }
                else
                    ret.CookedValue = ret.Value;


                item.EnqueueCookedVariable(ret.CookedValue);

                payLoad.Values[item.Name] = ret.CookedValue;

                ret.VarId = item.ID;

                item.Value = ret.Value;
                item.CookedValue = ret.CookedValue;
                item.StatusType = ret.StatusType;
                item.Timestamp = ret.Timestamp;
                item.Message = ret.Message;

                //变化了才推送到mqttserver，用于前端展示
                if (JsonConvert.SerializeObject(item.Values[1]) != JsonConvert.SerializeObject(item.Values[0]) || JsonConvert.SerializeObject(item.CookedValues[1]) != JsonConvert.SerializeObject(item.CookedValues[0]))
                {
                    var msgInternal = new InjectedMqttApplicationMessage(
                        new MqttApplicationMessage()
                        {
                            Topic =
                                $"internal/v1/gateway/telemetry/{Device.DeviceName}/{item.Name}",
                            PayloadSegment = Encoding.UTF8.GetBytes(
                                JsonConvert.SerializeObject(ret))
                        });
                    mqttServer.InjectApplicationMessage(msgInternal);
                }

                Thread.Sleep((int)Device.CmdPeriod);
            }
        }

        public void MyMqttClient_OnExcRpc(object? sender, RpcRequest e)
        {
            //设备名或者设备别名
            if (e.DeviceName == Device.DeviceName || Device.DeviceVariables.Select(x => x.Alias).Contains(e.DeviceName))
            {
                RpcLog rpcLog = new RpcLog()
                {
                    DeviceId = Device.ID,
                    StartTime = DateTime.Now,
                    Method = e.Method,
                    RpcSide = RpcSide.ServerSide,
                    Params = JsonConvert.SerializeObject(e.Params)
                };

                _logger.LogInformation($"{e.DeviceName}收到RPC,{e}");
                RpcResponse rpcResponse = new()
                { DeviceName = e.DeviceName, RequestId = e.RequestId, IsSuccess = false, Method = e.Method };
                //执行写入变量RPC
                if (e.Method.ToLower() == "write")
                {
                    resetEvent.Reset();

                    bool rpcConnected = false;
                    //没连接就连接
                    if (!Driver.IsConnected)
                        if (Driver.Connect())
                            rpcConnected = true;

                    //连接成功就尝试一个一个的写入，注意:目前写入地址和读取地址是相同的，对于PLC来说没问题，其他的要自己改........
                    if (Driver.IsConnected)
                    {
                        foreach (var para in e.Params)
                        {
                            //先查配置项，要用到配置的地址、数据类型、方法(方法最主要是用于区分写入数据的辅助判断，比如modbus不同的功能码)
                            //先找别名中的变量名，找不到就用设备名
                            DeviceVariable? deviceVariable;
                            if (e.DeviceName == Device.DeviceName)
                                deviceVariable = Device.DeviceVariables.FirstOrDefault(x =>
                                    x.Name == para.Key);
                            else
                                deviceVariable = Device.DeviceVariables.FirstOrDefault(x =>
                                    x.Name == para.Key && x.Alias == e.DeviceName);

                            if (deviceVariable != null && deviceVariable.ProtectType != ProtectTypeEnum.ReadOnly)
                            {
                                DriverAddressIoArgModel ioArgModel = new()
                                {
                                    Address = deviceVariable.DeviceAddress,
                                    Value = para.Value,
                                    ValueType = deviceVariable.DataType,
                                    EndianType = deviceVariable.EndianType
                                };
                                var writeResponse = Driver
                                    .WriteAsync(e.RequestId, deviceVariable.Method, ioArgModel).Result;
                                rpcResponse.IsSuccess = writeResponse.IsSuccess;
                                if (!writeResponse.IsSuccess)
                                {
                                    rpcResponse.Description += writeResponse.Description;
                                }
                            }
                            else
                            {
                                rpcResponse.IsSuccess = false;
                                rpcResponse.Description += $"未能找到支持写入的变量:{para.Key},";
                            }
                        }

                        if (rpcConnected)
                            Driver.Close();
                    }
                    else //连接失败
                    {
                        rpcResponse.IsSuccess = false;
                        rpcResponse.Description = $"{e.DeviceName} 连接失败";
                    }
                    resetEvent.Set();
                }
                //其他RPC TODO
                else
                {
                    rpcResponse.IsSuccess = false;
                    rpcResponse.Description = $"方法:{e.Method}暂未实现";
                }

                //反馈RPC
                _messageService.ResponseRpcAsync(rpcResponse).Wait();
                //纪录入库
                rpcLog.IsSuccess = rpcResponse.IsSuccess;
                rpcLog.Description = rpcResponse.Description;
                rpcLog.EndTime = DateTime.Now;


                using var dc = new DataContext(IoTBackgroundService.connnectSetting, IoTBackgroundService.DbType);
                dc.Set<RpcLog>().Add(rpcLog);
                dc.SaveChanges();
            }
        }

        public void StopThread()
        {
            _logger.LogInformation($"线程停止:{Device.DeviceName}");
            if (Device.DeviceVariables != null && Device.DeviceVariables.Any())
            {
                foreach (var deviceVariables in Device.DeviceVariables.GroupBy(x => x.Alias))
                {
                    string deviceName = string.IsNullOrWhiteSpace(deviceVariables.Key)
                        ? Device.DeviceName
                        : deviceVariables.Key;
                    _messageService?.DeviceDisconnected(deviceName, Device);
                }
            }

            if (_task == null) return;
            if (_messageService != null) _messageService.OnExcRpc -= MyMqttClient_OnExcRpc;
            _tokenSource.Cancel();
            Driver.Close();
        }

        public void Dispose()
        {
            Driver.Dispose();
            _interpreter = null;
            Methods = null;
            _logger.LogInformation($"线程释放,{Device.DeviceName}");
        }

        //mysql会把一些符号转义，没找到原因，先临时处理下
        private string DealMysqlStr(string expression)
        {
            return expression.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&").Replace("&quot;", "\"");
        }
    }
}