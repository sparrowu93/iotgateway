using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using PluginInterface;
using System.Runtime.CompilerServices;

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
            if (topicPath.Length < 2)
            {
                return new DriverReturnValueModel
                {
                    StatusType = VaribaleStatusTypeEnum.Bad,
                    Message = "Invalid address format. Expected format: topic.jsonPath"
                };
            }

            var topic = topicPath[0];
            var jsonPath = string.Join(".", topicPath.Skip(1));

            if (_subscriptions.TryGetValue(topic, out var subscription))
            {
                try 
                {
                    if (subscription.LastValue is JsonElement jsonElement)
                    {
                        JsonElement value;
                        if (jsonElement.TryGetProperty(jsonPath, out value))
                        {
                            return new DriverReturnValueModel
                            {
                                StatusType = VaribaleStatusTypeEnum.Good,
                                Value = GetJsonElementValue(value)
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

        private object GetJsonElementValue(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    return element.GetString();
                case JsonValueKind.Number:
                    if (element.TryGetInt32(out int intValue))
                        return intValue;
                    if (element.TryGetInt64(out long longValue))
                        return longValue;
                    return element.GetDouble();
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return element.GetBoolean();
                case JsonValueKind.Null:
                    return null;
                default:
                    return element.GetRawText();
            }
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
                    .WithPayload(JsonSerializer.Serialize(ioArg.Value))
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
        public object? LastValue { get; set; }
    }
}
