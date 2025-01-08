using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using PluginInterface;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MQTT.Driver.Models;

[assembly: InternalsVisibleTo("MQTT.Driver.Tests")]

namespace MQTT.Driver
{
    [DriverSupported("MQTTDevice")]
    [DriverInfo("MQTTDriver", "V1.0.0", "Copyright IoTGateway 2024-01-06, All Rights Reserved; 地址格式:topic.jsonPath")]
    public class MqttDriver : IDriver, IAddressDefinitionProvider
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
                        SubscribeToTopic(subscription.Topic);
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

        private void AddSubscription(string topic)
        {
            if (!_subscriptions.ContainsKey(topic))
            {
                _subscriptions[topic] = new MqttSubscription { Topic = topic };
            }
        }

        private async Task SubscribeToTopic(string topic)
        {
            if (_mqttClient?.IsConnected == true)
            {
                var mqttSubscribeOptions = new MqttClientSubscribeOptionsBuilder()
                    .WithTopicFilter(topic)
                    .Build();

                await _mqttClient.SubscribeAsync(mqttSubscribeOptions);
                AddSubscription(topic);
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
                    subscription.LastValue = JObject.Parse(payload);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error processing MQTT message");
            }

            return Task.CompletedTask;
        }

        [Method("读取", description: "读取MQTT主题的值")]
        public DriverReturnValueModel Read(DriverAddressIoArgModel ioArg)
        {
            var returnValue = new DriverReturnValueModel { StatusType = VaribaleStatusTypeEnum.Bad };

            try
            {
                // Split address into topic and JSON path
                var addressParts = ioArg.Address.Split(new[] { '.' }, 2);
                if (addressParts.Length != 2)
                {
                    returnValue.Message = "Invalid address format. Expected: topic.jsonPath";
                    return returnValue;
                }

                var topic = addressParts[0];
                var jsonPath = "$." + addressParts[1];

                // Ensure we're subscribed to the topic
                if (!_subscriptions.ContainsKey(topic))
                {
                    returnValue.Message = "Not subscribed to topic: " + topic;
                    return returnValue;
                }

                var subscription = _subscriptions[topic];
                if (subscription.LastValue == null)
                {
                    returnValue.Message = "No data received for topic: " + topic;
                    return returnValue;
                }

                // Extract value using JSON path
                var token = subscription.LastValue.SelectToken(jsonPath);
                if (token == null)
                {
                    returnValue.Message = "JSON path not found: " + jsonPath;
                    return returnValue;
                }

                returnValue.Value = token.ToObject<object>();
                returnValue.StatusType = VaribaleStatusTypeEnum.Good;
                return returnValue;
            }
            catch (Exception ex)
            {
                returnValue.Message = ex.Message;
                return returnValue;
            }
        }

        [Method("写入", description: "向MQTT主题写入值")]
        public async Task<RpcResponse> WriteAsync(string RequestId, string Method, DriverAddressIoArgModel ioArg)
        {
            if (!IsConnected)
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

        #region 变量地址格式定义区块
        public Dictionary<string, AddressDefinitionInfo> GetAddressDefinitions()
        {
            return MqttAddressDefinitions.GetDefinitions();
        }
        #endregion
    }

    internal class MqttSubscription
    {
        public string Topic { get; set; } = string.Empty;
        public JObject? LastValue { get; set; }
    }
}
