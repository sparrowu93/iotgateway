using MQTTnet;
using MQTTnet.Client;
using Newtonsoft.Json;
using PluginInterface;
using IoTGateway.Model;
using MQTTnet.Protocol;
using MQTTnet.Formatter;
using IoTGateway.DataAccess;
using PluginInterface.IoTSharp;
using PluginInterface.HuaWeiRoma;
using PluginInterface.ThingsBoard;
using Microsoft.Extensions.Logging;
using MQTTnet.Extensions.ManagedClient;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Net.WebSockets;
using System.Threading;

namespace Plugin
{
    public class MyMqttClient
    {
        private readonly ILogger<MyMqttClient> _logger;
        private readonly HttpClient _httpClient;
        private ClientWebSocket? _webSocket;
        private bool _isWebSocketConnected;
        private readonly CancellationTokenSource _webSocketCts;
        //private readonly ReferenceNodeManager? _uaNodeManager;

        private SystemConfig _systemConfig;
        private ManagedMqttClientOptions _options;
        public bool IsConnected => (_systemConfig.IoTPlatformType == IoTPlatformType.WebSocket ? _isWebSocketConnected : Client.IsConnected);
        private IManagedMqttClient? Client { get; set; }
        public event EventHandler<RpcRequest> OnExcRpc;
        public event EventHandler<ISAttributeResponse> OnReceiveAttributes;
        private readonly string _tbRpcTopic = "v1/gateway/rpc";

        public MyMqttClient(ILogger<MyMqttClient> logger)
        {
            _logger = logger;
            _httpClient = new HttpClient();
            _webSocketCts = new CancellationTokenSource();
            //_uaNodeManager = uaService.server.m_server.nodeManagers[0] as ReferenceNodeManager;

            StartClientAsync().Wait();
        }

        public async Task StartClientAsync()
        {
            try
            {
                await using var dc = new DataContext(IoTBackgroundService.connnectSetting, IoTBackgroundService.DbType);
                _systemConfig = dc.Set<SystemConfig>().First();

                if (_systemConfig.IoTPlatformType == IoTPlatformType.WebSocket)
                {
                    await InitializeWebSocketConnection();
                }
                else
                {
                    if (Client != null)
                    {
                        Client.Dispose();
                    }
                    Client = new MqttFactory().CreateManagedMqttClient();

                    #region ClientOptions

                    _options = new ManagedMqttClientOptionsBuilder()
                        .WithAutoReconnectDelay(TimeSpan.FromSeconds(5))
                        .WithMaxPendingMessages(100000)
                        .WithClientOptions(new MqttClientOptionsBuilder()
                            .WithClientId(string.IsNullOrEmpty(_systemConfig.ClientId)
                                ? Guid.NewGuid().ToString()
                                : _systemConfig.ClientId)
                            .WithTcpServer(_systemConfig.MqttIp, _systemConfig.MqttPort)
                            .WithCredentials(_systemConfig.MqttUName, _systemConfig.MqttUPwd)
                            .WithTimeout(TimeSpan.FromSeconds(30))
                            .WithKeepAlivePeriod(TimeSpan.FromSeconds(60))
                            .WithProtocolVersion(MqttProtocolVersion.V311)
                            .WithCleanSession(true)
                            .Build())
                        .Build();
                    #endregion

                    Client.ConnectedAsync += Client_ConnectedAsync;
                    Client.DisconnectedAsync += Client_DisconnectedAsync;
                    Client.ApplicationMessageReceivedAsync += Client_ApplicationMessageReceivedAsync;

                    await Client.StartAsync(_options);

                    _logger.LogInformation("MQTT WAITING FOR APPLICATION MESSAGES");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"StartClientAsync FAILED ");
            }
        }

        private async Task InitializeWebSocketConnection()
        {
            try
            {
                if (_webSocket != null)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Reconnecting", _webSocketCts.Token);
                    _webSocket.Dispose();
                }

                _webSocket = new ClientWebSocket();
                
                // 添加认证头（如果需要）
                if (!string.IsNullOrEmpty(_systemConfig.GatewayToken))
                {
                    _webSocket.Options.SetRequestHeader("Authorization", $"Bearer {_systemConfig.GatewayToken}");
                }

                var uri = new Uri(_systemConfig.HttpEndpoint);
                await _webSocket.ConnectAsync(uri, _webSocketCts.Token);
                _isWebSocketConnected = true;
                _logger.LogInformation("WebSocket connected successfully");

                // 启动接收消息的后台任务
                _ = Task.Run(async () => await ReceiveWebSocketMessages(), _webSocketCts.Token);
            }
            catch (Exception ex)
            {
                _isWebSocketConnected = false;
                _logger.LogError(ex, "Failed to initialize WebSocket connection");
                throw;
            }
        }

        private async Task ReceiveWebSocketMessages()
        {
            var buffer = new byte[4096];
            try
            {
                while (_webSocket.State == WebSocketState.Open && !_webSocketCts.Token.IsCancellationRequested)
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _webSocketCts.Token);
                    
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", _webSocketCts.Token);
                        _isWebSocketConnected = false;
                        break;
                    }

                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    _logger.LogDebug($"Received WebSocket message: {message}");
                    // 处理接收到的消息
                }
            }
            catch (Exception ex)
            {
                _isWebSocketConnected = false;
                _logger.LogError(ex, "Error in WebSocket receive loop");
            }
        }

        private async Task PublishTelemetryViaWebSocketAsync(string deviceName, Dictionary<string, List<PayLoad>> sendModel)
        {
            try
            {
                if (_webSocket?.State != WebSocketState.Open)
                {
                    await InitializeWebSocketConnection();
                }

                var message = JsonConvert.SerializeObject(new
                {
                    type = "telemetry",
                    device = deviceName,
                    data = sendModel
                });

                var messageBytes = Encoding.UTF8.GetBytes(message);
                await _webSocket.SendAsync(
                    new ArraySegment<byte>(messageBytes),
                    WebSocketMessageType.Text,
                    true,
                    _webSocketCts.Token
                );

                _logger.LogInformation($"Successfully published telemetry via WebSocket for device {deviceName}");
            }
            catch (Exception ex)
            {
                _isWebSocketConnected = false;
                _logger.LogError(ex, $"Failed to publish telemetry via WebSocket for device {deviceName}");
                throw;
            }
        }

        private async Task Client_ConnectedAsync(MqttClientConnectedEventArgs arg)
        {
            _logger.LogInformation($"MQTT CONNECTED WITH SERVER ");
            #region Topics
            try
            {
                switch (_systemConfig.IoTPlatformType)
                {
                    case IoTPlatformType.ThingsBoard:
                        //{"device": "Device A", "data": {"id": $request_id, "method": "toggle_gpio", "params": {"pin":1}}}
                        await Client.SubscribeAsync(_tbRpcTopic, MqttQualityOfServiceLevel.ExactlyOnce);
                        //Message: {"id": $request_id, "device": "Device A", "value": "value1"}
                        await Client.SubscribeAsync("v1/gateway/attributes/response", MqttQualityOfServiceLevel.ExactlyOnce);
                        //Message: {"device": "Device A", "data": {"attribute1": "value1", "attribute2": 42}}
                        await Client.SubscribeAsync("v1/gateway/attributes", MqttQualityOfServiceLevel.ExactlyOnce);
                        break;
                    case IoTPlatformType.IoTSharp:
                    case IoTPlatformType.IoTGateway:
                        await Client.SubscribeAsync("devices/+/rpc/request/+/+", MqttQualityOfServiceLevel.ExactlyOnce);
                        await Client.SubscribeAsync("devices/+/attributes/update", MqttQualityOfServiceLevel.ExactlyOnce);
                        //Message: {"device": "Device A", "data": {"attribute1": "value1", "attribute2": 42}}
                        await Client.SubscribeAsync("devices/+/attributes/response/+", MqttQualityOfServiceLevel.ExactlyOnce);
                        break;
                    case IoTPlatformType.ThingsCloud:
                        await Client.SubscribeAsync("gateway/attributes/response", MqttQualityOfServiceLevel.ExactlyOnce);
                        await Client.SubscribeAsync("gateway/attributes/get/response", MqttQualityOfServiceLevel.ExactlyOnce);
                        await Client.SubscribeAsync("gateway/attributes/push", MqttQualityOfServiceLevel.ExactlyOnce);
                        await Client.SubscribeAsync("gateway/event/response", MqttQualityOfServiceLevel.ExactlyOnce);
                        await Client.SubscribeAsync("gateway/command/send", MqttQualityOfServiceLevel.ExactlyOnce);
                        break;
                    case IoTPlatformType.AliCloudIoT:
                        break;
                    case IoTPlatformType.TencentIoTHub:
                        break;
                    case IoTPlatformType.BaiduIoTCore:
                        break;
                    case IoTPlatformType.OneNET:
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT Subscribe FAILED");
            }
            #endregion
        }

        private async Task Client_DisconnectedAsync(MqttClientDisconnectedEventArgs arg)
        {
            try
            {
                _logger.LogError($"MQTT DISCONNECTED WITH SERVER ");
                //await Client.ConnectAsync(_options);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT CONNECTING FAILED");
            }
        }

        private Task Client_ApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
        {
            _logger.LogDebug(
                $"ApplicationMessageReceived Topic {e.ApplicationMessage.Topic}  QualityOfServiceLevel:{e.ApplicationMessage.QualityOfServiceLevel} Retain:{e.ApplicationMessage.Retain} ");
            try
            {
                if (e.ApplicationMessage.Topic == _tbRpcTopic)
                    ReceiveTbRpc(e);
                else if (e.ApplicationMessage.Topic.StartsWith($"devices/") &&
                         e.ApplicationMessage.Topic.Contains("/response/"))
                {
                    ReceiveAttributes(e);
                }
                else if (e.ApplicationMessage.Topic.StartsWith($"devices/") &&
                         e.ApplicationMessage.Topic.Contains("/rpc/request/"))
                {
                    ReceiveIsRpc(e);
                }
                else if (e.ApplicationMessage.Topic == "gateway/command/send")
                {
                    ReceiveTcRpc(e);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex, $"ClientId:{e.ClientId} Topic:{e.ApplicationMessage.Topic},Payload:{e.ApplicationMessage.ConvertPayloadToString()}");
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// thingsboard rpc
        /// </summary>
        /// <param name="e"></param>
        private void ReceiveTbRpc(MqttApplicationMessageReceivedEventArgs e)
        {
            try
            {
                var tBRpcRequest =
                    JsonConvert.DeserializeObject<TBRpcRequest>(e.ApplicationMessage.ConvertPayloadToString());
                if (tBRpcRequest != null && !string.IsNullOrWhiteSpace(tBRpcRequest.RequestData.Method))
                {
                    OnExcRpc(Client, new RpcRequest()
                    {
                        Method = tBRpcRequest.RequestData.Method,
                        DeviceName = tBRpcRequest.DeviceName,
                        RequestId = tBRpcRequest.RequestData.RequestId,
                        Params = tBRpcRequest.RequestData.Params
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    $"ReceiveTbRpc:Topic:{e.ApplicationMessage.Topic},Payload:{e.ApplicationMessage.ConvertPayloadToString()}");
            }
        }

        /// <summary>
        /// thingscloud rpc
        /// </summary>
        /// <param name="e"></param>
        private void ReceiveTcRpc(MqttApplicationMessageReceivedEventArgs e)
        {
            try
            {
                var tCRpcRequest =
                    JsonConvert.DeserializeObject<TCRpcRequest>(e.ApplicationMessage.ConvertPayloadToString());
                if (tCRpcRequest != null)
                    OnExcRpc.Invoke(Client, new RpcRequest()
                    {
                        Method = tCRpcRequest.RequestData.Method,
                        DeviceName = tCRpcRequest.DeviceName,
                        RequestId = tCRpcRequest.RequestData.RequestId,
                        Params = tCRpcRequest.RequestData.Params
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    $"ReceiveTbRpc:Topic:{e.ApplicationMessage.Topic},Payload:{e.ApplicationMessage.ConvertPayloadToString()}");
            }
        }

        private void ReceiveIsRpc(MqttApplicationMessageReceivedEventArgs e)
        {
            try
            {
                var tps = e.ApplicationMessage.Topic.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                var rpcMethodName = tps[4];
                var rpcDeviceName = tps[1];
                var rpcRequestId = tps[5];
                _logger.LogInformation($"rpcMethodName={rpcMethodName} ");
                _logger.LogInformation($"rpcDeviceName={rpcDeviceName} ");
                _logger.LogInformation($"rpcRequestId={rpcRequestId}   ");
                if (!string.IsNullOrEmpty(rpcMethodName) && !string.IsNullOrEmpty(rpcDeviceName) &&
                    !string.IsNullOrEmpty(rpcRequestId))
                {
                    Task.Run(() =>
                    {
                        OnExcRpc(Client, new RpcRequest()
                        {
                            Method = rpcMethodName,
                            DeviceName = rpcDeviceName,
                            RequestId = rpcRequestId,
                            Params = JsonConvert.DeserializeObject<Dictionary<string, object>>(e.ApplicationMessage
                                .ConvertPayloadToString())
                        });
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    $"ReceiveIsRpc:Topic:{e.ApplicationMessage.Topic},Payload:{e.ApplicationMessage.ConvertPayloadToString()}");
            }
        }

        private async Task ResponseTbRpcAsync(TBRpcResponse tBRpcResponse)
        {
            await Client.EnqueueAsync(new MqttApplicationMessageBuilder()
                .WithTopic(_tbRpcTopic)
                .WithPayload(JsonConvert.SerializeObject(tBRpcResponse))
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce).Build());
        }

        private async Task ResponseTcRpcAsync(TCRpcRequest tCRpcResponse)
        {
            var topic = $"command/reply/{tCRpcResponse.RequestData.RequestId}";
            await Client.EnqueueAsync(new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(JsonConvert.SerializeObject(tCRpcResponse))
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce).Build());
        }

        private async Task ResponseIsRpcAsync(ISRpcResponse rpcResult)
        {
            //var responseTopic = $"/devices/{deviceid}/rpc/response/{methodName}/{rpcid}";
            var topic = $"devices/{rpcResult.DeviceId}/rpc/response/{rpcResult.Method}/{rpcResult.ResponseId}";
            await Client.EnqueueAsync(new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(JsonConvert.SerializeObject(rpcResult))
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce).Build());
        }

        private void ReceiveAttributes(MqttApplicationMessageReceivedEventArgs e)
        {
            var tps = e.ApplicationMessage.Topic.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var rpcMethodName = tps[2];
            var rpcDeviceName = tps[1];
            var rpcRequestId = tps[4];
            _logger.LogInformation($"rpcMethodName={rpcMethodName}");
            _logger.LogInformation($"rpcDeviceName={rpcDeviceName}");
            _logger.LogInformation($"rpcRequestId={rpcRequestId}");

            if (!string.IsNullOrEmpty(rpcMethodName) && !string.IsNullOrEmpty(rpcDeviceName) &&
                !string.IsNullOrEmpty(rpcRequestId))
            {
                if (e.ApplicationMessage.Topic.Contains("/attributes/"))
                {
                    OnReceiveAttributes.Invoke(Client, new ISAttributeResponse()
                    {
                        KeyName = rpcMethodName,
                        DeviceName = rpcDeviceName,
                        Id = rpcRequestId,
                        Data = e.ApplicationMessage.ConvertPayloadToString()
                    });
                }
            }
        }

        public Task UploadAttributeAsync(string deviceName, object obj)
        {
            //Topic: v1/gateway/attributes
            //Message: {"Device A":{"attribute1":"value1", "attribute2": 42}, "Device B":{"attribute1":"value1", "attribute2": 42}
            try
            {
                return Client.EnqueueAsync(new MqttApplicationMessageBuilder()
                    .WithTopic($"devices/{deviceName}/attributes").WithPayload(JsonConvert.SerializeObject(obj))
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
                    .Build());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Device:{deviceName} UploadAttributeAsync Failed");
            }

            return Task.CompletedTask;
        }

        public async Task UploadIsTelemetryDataAsync(string deviceName, object obj)
        {
            await Client.EnqueueAsync(new MqttApplicationMessageBuilder().WithTopic($"devices/{deviceName}/telemetry")
                .WithPayload(JsonConvert.SerializeObject(obj)).Build());
        }

        public async Task UploadTcTelemetryDataAsync(string deviceName, object obj)
        {
            var toSend = new Dictionary<string, object> { { deviceName, obj } };
            await Client.EnqueueAsync(new MqttApplicationMessageBuilder().WithTopic($"gateway/attributes")
                .WithPayload(JsonConvert.SerializeObject(toSend)).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce).Build());
        }

        public async Task UploadHwTelemetryDataAsync(Device device, object obj)
        {
            var hwTelemetry = new List<HwTelemetry>()
            {
                new HwTelemetry()
                {
                    DeviceId = device.DeviceConfigs.FirstOrDefault(x => x.DeviceConfigName == "DeviceId")?.Value,
                    Services = new()
                    {
                        new Service()
                        {
                            ServiceId = "serviceId",
                            EventTime = DateTime.Now.ToString("yyyyMMddTHHmmssZ"),
                            Data = obj
                        }
                    }
                }
            };
            var hwTelemetrys = new HwTelemetrys()
            {
                Devices = hwTelemetry
            };

            await Client.EnqueueAsync(new MqttApplicationMessageBuilder().WithTopic($"/v1/devices/{_systemConfig.GatewayName}/datas")
                .WithPayload(JsonConvert.SerializeObject(hwTelemetrys)).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce).Build());
        }

        public async Task ResponseRpcAsync(RpcResponse rpcResponse)
        {
            try
            {
                switch (_systemConfig.IoTPlatformType)
                {
                    case IoTPlatformType.ThingsBoard:
                        var tRpcResponse = new TBRpcResponse
                        {
                            DeviceName = rpcResponse.DeviceName,
                            RequestId = rpcResponse.RequestId,
                            ResponseData = new Dictionary<string, object>
                                { { "success", rpcResponse.IsSuccess }, { "description", rpcResponse.Description } }
                        };
                        await ResponseTbRpcAsync(tRpcResponse);
                        break;
                    case IoTPlatformType.IoTSharp:
                    case IoTPlatformType.IoTGateway:
                        await ResponseIsRpcAsync(new ISRpcResponse
                        {
                            DeviceId = rpcResponse.DeviceName,
                            Method = rpcResponse.Method,
                            ResponseId = rpcResponse.RequestId,
                            Data = JsonConvert.SerializeObject(new Dictionary<string, object>
                            {
                                { "success", rpcResponse.IsSuccess }, { "description", rpcResponse.Description }
                            })
                        });
                        break;
                    case IoTPlatformType.ThingsCloud:
                        //官网API不需要回复的                        
                        break;
                    case IoTPlatformType.AliCloudIoT:
                        break;
                    case IoTPlatformType.TencentIoTHub:
                        break;
                    case IoTPlatformType.BaiduIoTCore:
                        break;
                    case IoTPlatformType.OneNET:
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"ResponseRpc Error,{rpcResponse}");
            }
        }

        public async Task RequestAttributes(string deviceName, bool anySide, params string[] args)
        {
            try
            {
                string id = Guid.NewGuid().ToString();
                switch (_systemConfig.IoTPlatformType)
                {
                    case IoTPlatformType.ThingsBoard:
                        //{"id": $request_id, "device": "Device A", "client": true, "key": "attribute1"}
                        Dictionary<string, object> tbRequestData = new Dictionary<string, object>
                        {
                            { "id", id },
                            { "device", deviceName },
                            { "client", true },
                            { "key", args[0] }
                        };
                        await Client.EnqueueAsync(new MqttApplicationMessageBuilder().WithTopic($"v1/gateway/attributes/request")
                            .WithPayload(JsonConvert.SerializeObject(tbRequestData)).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce).Build());
                        break;
                    case IoTPlatformType.IoTSharp:
                    case IoTPlatformType.IoTGateway:
                        string topic = $"devices/{deviceName}/attributes/request/{id}";
                        Dictionary<string, string> keys = new Dictionary<string, string>();
                        keys.Add(anySide ? "anySide" : "server", string.Join(",", args));
                        await Client.SubscribeAsync($"devices/{deviceName}/attributes/response/{id}",
                            MqttQualityOfServiceLevel.ExactlyOnce);
                        await Client.EnqueueAsync(new MqttApplicationMessageBuilder().WithTopic(topic)
                            .WithPayload(JsonConvert.SerializeObject(keys))
                            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce).Build());
                        break;
                    case IoTPlatformType.AliCloudIoT:
                        break;
                    case IoTPlatformType.TencentIoTHub:
                        break;
                    case IoTPlatformType.BaiduIoTCore:
                        break;
                    case IoTPlatformType.OneNET:
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"RequestAttributes:{deviceName}");
            }
        }

        private Dictionary<string, List<PayLoad>> _lastTelemetrys = new(0);

        /// <summary>
        /// 判断是否推送遥测数据
        /// </summary>
        /// <param name="device">设备</param>
        /// <param name="sendModel">遥测</param>
        /// <returns></returns>
        private bool CanPubTelemetry(string DeviceName, Device device, Dictionary<string, List<PayLoad>> sendModel)
        {
            bool canPub = false;
            try
            {//第一次上传
                if (!_lastTelemetrys.ContainsKey(DeviceName))
                    canPub = true;
                else
                {
                    //变化上传
                    if (device.CgUpload)
                    {
                        //是否超过归档周期
                        if (sendModel[DeviceName][0].TS - _lastTelemetrys[DeviceName][0].TS >
                            device.EnforcePeriod)
                            canPub = true;
                        //是否变化 这里不好先用
                        else
                        {
                            if (JsonConvert.SerializeObject(sendModel[DeviceName][0].Values) !=
                                JsonConvert.SerializeObject(_lastTelemetrys[DeviceName][0].Values))
                                canPub = true;
                        }
                    }
                    //非变化上传
                    else
                        canPub = true;
                }
            }
            catch (Exception e)
            {
                canPub = true;
                Console.WriteLine(e);
            }

            if (canPub)
                _lastTelemetrys[DeviceName] = sendModel[DeviceName];
            return canPub;
        }

        public async Task PublishTelemetryAsync(string deviceName, Device device, Dictionary<string, List<PayLoad>> sendModel)
        {
            try
            {
                if (CanPubTelemetry(deviceName, device, sendModel))
                {
                    switch (_systemConfig.IoTPlatformType)
                    {
                        case IoTPlatformType.ThingsBoard:
                            await Client.EnqueueAsync(new MqttApplicationMessageBuilder().WithTopic("v1/gateway/telemetry")
                                .WithPayload(JsonConvert.SerializeObject(sendModel))
                                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce).Build());
                            break;
                        case IoTPlatformType.IoTSharp:
                        case IoTPlatformType.IoTGateway:
                            foreach (var payload in sendModel[deviceName])
                            {
                                if (payload.Values != null)
                                {
                                    if (_systemConfig.IoTPlatformType == IoTPlatformType.IoTGateway)
                                        payload.Values["_ts_"] = (long)(DateTime.UtcNow - _tsStartDt).TotalMilliseconds;
                                    await UploadIsTelemetryDataAsync(deviceName, payload.Values);
                                }
                            }
                            break;
                        case IoTPlatformType.ThingsCloud:
                            foreach (var payload in sendModel[deviceName])
                            {
                                if (payload.Values != null)
                                    await UploadTcTelemetryDataAsync(deviceName, payload.Values);
                            }
                            break;
                        case IoTPlatformType.HuaWei:
                            foreach (var payload in sendModel[deviceName])
                            {
                                if (payload.Values != null)
                                    await UploadHwTelemetryDataAsync(device, payload.Values);
                            }
                            break;
                        case IoTPlatformType.HTTP:
                            await PublishTelemetryViaHttpAsync(deviceName, sendModel);
                            break;
                        case IoTPlatformType.WebSocket:
                            await PublishTelemetryViaWebSocketAsync(deviceName, sendModel);
                            break;
                        case IoTPlatformType.AliCloudIoT:
                        case IoTPlatformType.TencentIoTHub:
                        case IoTPlatformType.BaiduIoTCore:
                        case IoTPlatformType.OneNET:
                        default:
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"PublishTelemetryAsync Error");
            }
        }

        private async Task PublishTelemetryViaHttpAsync(string deviceName, Dictionary<string, List<PayLoad>> sendModel)
        {
            var url = $"{_systemConfig.HttpEndpoint}/api/v1/{_systemConfig.GatewayToken}/telemetry";
            var content = new StringContent(JsonConvert.SerializeObject(sendModel), Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync(url, content);
                response.EnsureSuccessStatusCode();
                _logger.LogInformation($"Successfully published telemetry via HTTP for device {deviceName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to publish telemetry via HTTP for device {deviceName}");
                throw;
            }
        }

        private readonly DateTime _tsStartDt = new(1970, 1, 1);

        public async Task DeviceConnected(string DeviceName, Device device)
        {
            try
            {
                switch (_systemConfig.IoTPlatformType)
                {
                    case IoTPlatformType.ThingsBoard:
                    case IoTPlatformType.IoTSharp:
                    case IoTPlatformType.IoTGateway:
                        await Client.EnqueueAsync(new MqttApplicationMessageBuilder().WithTopic("v1/gateway/connect")
                            .WithPayload(JsonConvert.SerializeObject(new Dictionary<string, string>
                                { { "device", DeviceName } }))
                            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce).Build());
                        break;
                    case IoTPlatformType.AliCloudIoT:
                        break;
                    case IoTPlatformType.TencentIoTHub:
                        break;
                    case IoTPlatformType.BaiduIoTCore:
                        break;
                    case IoTPlatformType.OneNET:
                        break;
                    case IoTPlatformType.ThingsCloud:
                        await Client.EnqueueAsync(new MqttApplicationMessageBuilder().WithTopic("gateway/connect")
                            .WithPayload(JsonConvert.SerializeObject(new Dictionary<string, string>
                                { { "device", DeviceName } }))
                            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce).Build());
                        break;
                    case IoTPlatformType.HuaWei:
                        var deviceOnLine = new HwDeviceOnOffLine()
                        {
                            MId = new Random().Next(0, 1024), //命令ID
                            DeviceStatuses = new List<DeviceStatus>()
                            {
                                new DeviceStatus()
                                {
                                    DeviceId = device.DeviceConfigs.FirstOrDefault(x => x.DeviceConfigName == "DeviceId")?.Value,
                                    Status = "ONLINE"
                                }
                            }
                        };
                        await Client.EnqueueAsync(new MqttApplicationMessageBuilder().WithTopic($"/v1/devices/{_systemConfig.GatewayName}/topo/update")
                            .WithPayload(JsonConvert.SerializeObject(deviceOnLine))
                            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce).Build());
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"DeviceConnected:{DeviceName}");
            }
        }

        public async Task DeviceDisconnected(string DeviceName, Device device)
        {
            try
            {
                switch (_systemConfig.IoTPlatformType)
                {
                    case IoTPlatformType.ThingsBoard:
                    case IoTPlatformType.IoTSharp:
                    case IoTPlatformType.IoTGateway:
                        await Client.EnqueueAsync(new MqttApplicationMessageBuilder().WithTopic($"v1/gateway/disconnect")
                            .WithPayload(JsonConvert.SerializeObject(new Dictionary<string, string>
                                { { "device", DeviceName } }))
                            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce).Build());
                        break;
                    case IoTPlatformType.AliCloudIoT:
                        break;
                    case IoTPlatformType.TencentIoTHub:
                        break;
                    case IoTPlatformType.BaiduIoTCore:
                        break;
                    case IoTPlatformType.OneNET:
                        break;
                    case IoTPlatformType.ThingsCloud:
                        await Client.EnqueueAsync(new MqttApplicationMessageBuilder().WithTopic($"gateway/disconnect")
                            .WithPayload(JsonConvert.SerializeObject(new Dictionary<string, string>
                                { { "device", DeviceName } }))
                            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce).Build());
                        break;
                    case IoTPlatformType.HuaWei:
                        var deviceOnLine = new HwDeviceOnOffLine()
                        {
                            MId = new Random().Next(0, 1024), //命令ID
                            DeviceStatuses = new List<DeviceStatus>()
                            {
                                new DeviceStatus()
                                {
                                    DeviceId = device.DeviceConfigs.FirstOrDefault(x => x.DeviceConfigName == "DeviceId")?.Value,
                                    Status = "OFFLINE"
                                }
                            }
                        };
                        await Client.EnqueueAsync(new MqttApplicationMessageBuilder().WithTopic($"/v1/devices/{_systemConfig.GatewayName}/topo/update")
                            .WithPayload(JsonConvert.SerializeObject(deviceOnLine))
                            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce).Build());
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"DeviceDisconnected:{DeviceName}");
            }
        }

        public async Task DeviceAdded(Device device)
        {
            try
            {
                switch (_systemConfig.IoTPlatformType)
                {
                    case IoTPlatformType.HuaWei:
                        var topic = $"/v1/devices/{_systemConfig.GatewayName}/topo/add";

                        var addDeviceDto = new HwAddDeviceDto
                        {
                            MId = new Random().Next(0, 1024), //命令ID
                        };
                        addDeviceDto.DeviceInfos.Add(
                            new DeviceInfo
                            {
                                NodeId = device.DeviceName,
                                Name = device.DeviceName,
                                Description = device.Description,
                                ManufacturerId = "Test_n",
                                ProductType = "A_n"
                            }
                        );
                        await Client.EnqueueAsync(new MqttApplicationMessageBuilder().WithTopic(topic)
                            .WithPayload(JsonConvert.SerializeObject(addDeviceDto))
                            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce).Build());
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"DeviceAdded:{device.DeviceName}");
            }
        }

        public async Task DeviceDeleted(Device device)
        {
            try
            {
                switch (_systemConfig.IoTPlatformType)
                {
                    case IoTPlatformType.HuaWei:
                        var topic = $"/v1/devices/{_systemConfig.GatewayName}/topo/delete";

                        await using (var dc = new DataContext(IoTBackgroundService.connnectSetting, IoTBackgroundService.DbType))
                        {
                            var deviceId = dc.Set<DeviceConfig>().FirstOrDefault(x =>
                                x.DeviceId == device.ID && x.DeviceConfigName == "DeviceId")?.Value;

                            var deleteDeviceDto = new HwDeleteDeviceDto
                            {
                                Id = new Random().Next(0, 1024), //命令ID
                                DeviceId = deviceId,
                                RequestTime = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds,
                                Request = new()
                                {
                                    ManufacturerId = "Test_n",
                                    ManufacturerName = "Test_n",
                                    ProductType = "A_n"
                                }
                            };
                            await Client.EnqueueAsync(new MqttApplicationMessageBuilder().WithTopic(topic)
                                .WithPayload(JsonConvert.SerializeObject(deleteDeviceDto))
                                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce).Build());
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"DeviceAdded:{device.DeviceName}");
            }
        }
    }
}