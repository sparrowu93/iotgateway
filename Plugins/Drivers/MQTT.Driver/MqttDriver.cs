using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using PluginInterface;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;

[assembly: InternalsVisibleTo("MQTT.Driver.Tests")]

namespace MQTT.Driver
{
    [DriverSupported("MQTTDevice")]
    [DriverInfo("MQTTDriver", "V1.0.0", "Copyright IoTGateway 2024-01-06, All Rights Reserved; 地址格式:topic.jsonPath")]
    public class MqttDriver : IDriver
    {
        private readonly IMqttClient _mqttClient;
        private MqttClientOptions _options;
        private readonly Dictionary<string, MqttSubscription> _subscriptions;
        private bool _isConnected;
        public ILogger _logger { get; set; }

        #region Configuration Parameters
        [ConfigParameter("设备Id")]
        public string DeviceId { get; set; }

        [ConfigParameter("Broker地址")]
        public string BrokerAddress { get; set; } = "127.0.0.1";

        [ConfigParameter("Broker端口")]
        public int Port { get; set; } = 1883;

        [ConfigParameter("用户名")]
        public string Username { get; set; }

        [ConfigParameter("密码")]
        public string Password { get; set; }

        [ConfigParameter("超时时间ms")]
        public int Timeout { get; set; } = 3000;

        [ConfigParameter("最小通讯周期ms")]
        public uint MinPeriod { get; set; } = 3000;

        [ConfigParameter("客户端ID")]
        public string ClientId { get; set; }
        #endregion

        public bool IsConnected => _isConnected;

        public MqttDriver(string device, ILogger logger)
        {
            DeviceId = device;
            _logger = logger;
            _subscriptions = new Dictionary<string, MqttSubscription>();
            var factory = new MqttFactory();
            _mqttClient = factory.CreateMqttClient();
            _mqttClient.ApplicationMessageReceivedAsync += HandleMessageReceived;

            _logger.LogInformation($"Device:[{device}],Create()");
        }

        public bool Connect()
        {
            try
            {
                var optionsBuilder = new MqttClientOptionsBuilder()
                    .WithTcpServer(BrokerAddress, Port);

                if (!string.IsNullOrEmpty(Username))
                {
                    optionsBuilder.WithCredentials(Username, Password);
                }

                if (!string.IsNullOrEmpty(ClientId))
                {
                    optionsBuilder.WithClientId(ClientId);
                }

                _options = optionsBuilder.Build();

                var result = _mqttClient.ConnectAsync(_options).GetAwaiter().GetResult();
                _isConnected = result.ResultCode == MqttClientConnectResultCode.Success;

                if (_isConnected)
                {
                    // Resubscribe to all topics
                    foreach (var subscription in _subscriptions.Values)
                    {
                        SubscribeToTopic(subscription.Topic, subscription.Parser);
                    }
                }

                return _isConnected;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to connect to MQTT broker");
                return false;
            }
        }

        public bool Close()
        {
            try
            {
                _mqttClient.DisconnectAsync().GetAwaiter().GetResult();
                _isConnected = false;
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to disconnect from MQTT broker");
                return false;
            }
        }

        public void AddSubscription(string topic, Func<string, object> parser)
        {
            _subscriptions[topic] = new MqttSubscription
            {
                Topic = topic,
                Parser = parser,
                LastValue = null
            };

            if (_isConnected)
            {
                SubscribeToTopic(topic, parser);
            }
        }

        private async void SubscribeToTopic(string topic, Func<string, object> parser)
        {
            try
            {
                var mqttSubscribeOptions = new MqttFactory().CreateSubscribeOptionsBuilder()
                    .WithTopicFilter(f => f.WithTopic(topic))
                    .Build();

                await _mqttClient.SubscribeAsync(mqttSubscribeOptions);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Failed to subscribe to topic: {topic}");
            }
        }

        private Task HandleMessageReceived(MqttApplicationMessageReceivedEventArgs e)
        {
            try
            {
                var topic = e.ApplicationMessage.Topic;
                if (_subscriptions.TryGetValue(topic, out var subscription))
                {
                    var payload = System.Text.Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
                    subscription.LastValue = subscription.Parser(payload);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing MQTT message");
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 读取MQTT主题下的数据，支持JSON路径解析
        /// </summary>
        /// <remarks>
        /// JSON路径解析示例：
        /// 1. 基础类型示例：
        ///    - 主题：device/temperature
        ///    - JSON数据：{"value": 25.5, "unit": "C"}
        ///    - 地址：device/temperature.value -> 返回 25.5
        ///    - 地址：device/temperature.unit -> 返回 "C"
        /// 
        /// 2. 数组访问示例：
        ///    - 主题：device/sensors
        ///    - JSON数据：{"data": [{"id": 1, "value": 25}, {"id": 2, "value": 30}]}
        ///    - 地址：device/sensors.data[0].value -> 返回 25
        ///    - 地址：device/sensors.data[1].value -> 返回 30
        /// 
        /// 3. 嵌套对象示例：
        ///    - 主题：device/status
        ///    - JSON数据：{"system": {"power": {"voltage": 220, "current": 5}}}
        ///    - 地址：device/status.system.power.voltage -> 返回 220
        ///    - 地址：device/status.system.power.current -> 返回 5
        /// 
        /// 4. 复杂数据结构示例：
        ///    - 主题：device/metrics
        ///    - JSON数据：{
        ///        "timestamp": "2023-01-01T12:00:00Z",
        ///        "measurements": {
        ///          "temperature": [
        ///            {"location": "room1", "value": 22},
        ///            {"location": "room2", "value": 24}
        ///          ]
        ///        }
        ///      }
        ///    - 地址：device/metrics.measurements.temperature[0].value -> 返回 22
        ///    - 地址：device/metrics.measurements.temperature[1].location -> 返回 "room2"
        /// </remarks>
        /// <param name="ioArg">包含地址信息的参数模型</param>
        /// <returns>返回解析后的数据模型</returns>
        public DriverReturnValueModel Read(DriverAddressIoArgModel ioArg)
        {
            if (!_isConnected)
            {
                return new DriverReturnValueModel 
                { 
                    StatusType = VaribaleStatusTypeEnum.Bad,
                    Message = "Not connected to MQTT broker" 
                };
            }

            var topicPath = ioArg.Address.Split('.');

            var topic = topicPath[0];
            var jsonPath = string.Join(".", topicPath.Skip(1));

            if (_subscriptions.TryGetValue(topic, out var subscription))
            {
                try 
                {
                    if (subscription.LastValue is JObject jsonElement)
                    {
                        if (string.IsNullOrEmpty(jsonPath))
                        {
                            return new DriverReturnValueModel
                            {
                                StatusType = VaribaleStatusTypeEnum.Good,
                                Value = GetJObjectValue(jsonElement)
                            };
                        }
                        JToken value;
                        if (jsonElement.TryGetValue(jsonPath, out value))
                        {
                            return new DriverReturnValueModel
                            {
                                StatusType = VaribaleStatusTypeEnum.Good,
                                Value = GetJTokenValue(value)
                            };
                        }
                    }
                    return new DriverReturnValueModel
                    {
                        StatusType = VaribaleStatusTypeEnum.Bad,
                        Message = $"JSON path '{jsonPath}' not found in topic: {topic}"
                    };
                }
                catch (Exception ex)
                {
                    return new DriverReturnValueModel
                    {
                        StatusType = VaribaleStatusTypeEnum.Bad,
                        Message = $"Error parsing JSON data: {ex.Message}"
                    };
                }
            }

            return new DriverReturnValueModel
            {
                StatusType = VaribaleStatusTypeEnum.Bad,
                Message = $"No subscription found for topic: {topic}"
            };
        }

        private object GetJObjectValue(JObject element)
        {
            return element;
        }

        private object GetJTokenValue(JToken token)
        {
            if (token is JValue value)
            {
                return value.Value;
            }
            return token;
        }

        public async Task<RpcResponse> WriteAsync(string RequestId, string Method, DriverAddressIoArgModel ioArg)
        {
            if (!_isConnected)
            {
                return new RpcResponse { IsSuccess = false, Description = "Not connected to MQTT broker" };
            }

            try
            {
                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(ioArg.Address)
                    .WithPayload(JsonConvert.SerializeObject(ioArg.Value))
                    .Build();

                await _mqttClient.PublishAsync(message);

                return new RpcResponse { IsSuccess = true };
            }
            catch (Exception ex)
            {
                return new RpcResponse
                {
                    IsSuccess = false,
                    Description = $"Failed to publish message: {ex.Message}"
                };
            }
        }

        public void Dispose()
        {
            if (_isConnected)
            {
                Close();
            }
            _mqttClient?.Dispose();
        }
    }

    internal class MqttSubscription
    {
        public string Topic { get; set; } = string.Empty;
        public Func<string, object>? Parser { get; set; }
        public JObject? LastValue { get; set; }
    }
}
