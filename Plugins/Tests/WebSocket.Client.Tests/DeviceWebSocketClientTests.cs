using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PluginInterface;
using WebSocket.Client.Tests.TestServer;

namespace WebSocket.Client.Tests
{
    [TestClass]
    public class DeviceWebSocketClientTests : IDisposable
    {
        private Mock<ILogger<DeviceWebSocketClient>> _loggerMock;
        private DeviceWebSocketClient _client;
        private TestWebSocketServer? _server;
        private const string DeviceId = "test_device";
        private const string ServerListenUrl = "http://localhost:8181/ws/";
        private const string ClientConnectUrl = "ws://localhost:8181/ws/";
        private readonly ManualResetEventSlim _messageReceivedEvent = new(false);
        private readonly ManualResetEventSlim _clientConnectedEvent = new(false);
        private readonly ManualResetEventSlim _clientDisconnectedEvent = new(false);
        private string? _lastReceivedMessage;

        [TestInitialize]
        public void Setup()
        {
            _loggerMock = new Mock<ILogger<DeviceWebSocketClient>>();
            _client = new DeviceWebSocketClient(DeviceId, _loggerMock.Object)
            {
                ServerUrl = ClientConnectUrl,
                ReconnectInterval = 1000,
                HeartbeatInterval = 1000,
                ConnectionTimeout = 2000
            };

            // Create test server
            _server = new TestWebSocketServer(ServerListenUrl, _loggerMock.Object);
            _server.OnMessageReceived = message => 
            {
                _lastReceivedMessage = message;
                _messageReceivedEvent.Set();
            };
            _server.OnClientConnected = () => _clientConnectedEvent.Set();
            _server.OnClientDisconnected = () => _clientDisconnectedEvent.Set();
            _server.Start();
        }

        [TestCleanup]
        public void Cleanup()
        {
            _client?.Dispose();
            _server?.Dispose();
            _messageReceivedEvent.Reset();
            _clientConnectedEvent.Reset();
            _clientDisconnectedEvent.Reset();
        }

        public void Dispose()
        {
            _messageReceivedEvent.Dispose();
            _clientConnectedEvent.Dispose();
            _clientDisconnectedEvent.Dispose();
        }

        [TestMethod]
        public void TestConnection_ShouldConnectSuccessfully()
        {
            // Act
            var connected = _client.Connect();
            var clientConnected = _clientConnectedEvent.Wait(TimeSpan.FromSeconds(5));

            // Assert
            Assert.IsTrue(connected, "Client should connect successfully");
            Assert.IsTrue(clientConnected, "Server should receive client connection");
            Assert.IsTrue(_client.IsConnected, "Client should report connected status");
        }

        [TestMethod]
        public void TestHeartbeat_ShouldSendAndReceiveHeartbeat()
        {
            // Arrange
            _client.Connect();
            Assert.IsTrue(_clientConnectedEvent.Wait(TimeSpan.FromSeconds(5)), "Client should connect");

            // Act & Assert
            // 等待心跳消息
            var heartbeatReceived = _messageReceivedEvent.Wait(TimeSpan.FromSeconds(5));
            Assert.IsTrue(heartbeatReceived, "Server should receive heartbeat message");
            Assert.IsNotNull(_lastReceivedMessage);
            Assert.IsTrue(_lastReceivedMessage.Contains("heartbeat"), "Message should be a heartbeat");
        }

        [TestMethod]
        public void TestReconnection_ShouldReconnectAfterDisconnection()
        {
            // Arrange
            _client.Connect();
            Assert.IsTrue(_clientConnectedEvent.Wait(TimeSpan.FromSeconds(5)), "Initial connection should succeed");
            Assert.IsTrue(_client.IsConnected, "Client should report connected status after initial connection");

            // Act
            _loggerMock.Object.LogInformation("Stopping server to trigger disconnection");
            _server?.Stop();
            
            // Wait for client to handle disconnection
            Assert.IsTrue(_clientDisconnectedEvent.Wait(TimeSpan.FromSeconds(5)), "Client should disconnect");
            
            // Add a small delay to ensure the client has fully processed the disconnection
            Thread.Sleep(100);
            Assert.IsFalse(_client.IsConnected, "Client should report disconnected status after server stop");
            
            // Reset events and restart server
            _clientConnectedEvent.Reset();
            _loggerMock.Object.LogInformation("Restarting server");
            _server?.Start();

            // Assert
            _loggerMock.Object.LogInformation("Waiting for reconnection...");
            var reconnected = _clientConnectedEvent.Wait(TimeSpan.FromSeconds(10));
            Assert.IsTrue(reconnected, "Client should reconnect after server restart");
            Assert.IsTrue(_client.IsConnected, "Client should report connected status after reconnection");
        }

        [TestMethod]
        public void TestConnection_ShouldHandleConnectionTimeout()
        {
            // Arrange
            _server?.Stop(); // 确保服务器未运行
            _client.ConnectionTimeout = 1000; // 设置较短的超时时间

            // Act
            var connected = _client.Connect();

            // Assert
            Assert.IsFalse(connected, "Connection should fail due to timeout");
            Assert.IsFalse(_client.IsConnected, "Client should report disconnected status");
        }

        [TestMethod]
        public void TestConnection_ShouldHandleInvalidUrl()
        {
            // Arrange
            _client.ServerUrl = "ws://invalid:99999/"; // 使用无效的URL

            // Act
            var connected = _client.Connect();

            // Assert
            Assert.IsFalse(connected, "Connection should fail with invalid URL");
            Assert.IsFalse(_client.IsConnected, "Client should report disconnected status");
        }

        [TestMethod]
        public void TestGracefulDisconnection_ShouldCloseCleanly()
        {
            // Arrange
            _client.Connect();
            Assert.IsTrue(_clientConnectedEvent.Wait(TimeSpan.FromSeconds(5)), "Client should connect");

            // Act
            var closed = _client.Close();
            var disconnected = _clientDisconnectedEvent.Wait(TimeSpan.FromSeconds(5));

            // Assert
            Assert.IsTrue(closed, "Close should succeed");
            Assert.IsTrue(disconnected, "Server should detect client disconnection");
            Assert.IsFalse(_client.IsConnected, "Client should report disconnected status");
        }

        #region JSON Value Tests
        [TestMethod]
        public void TestAddressDefinitions_ShouldProvideValidDefinitions()
        {
            // Act
            var definitions = _client.GetAddressDefinitions();

            // Assert
            Assert.IsNotNull(definitions);
            Assert.IsTrue(definitions.Count > 0);

            // 验证温度地址定义
            Assert.IsTrue(definitions.ContainsKey("temperature"));
            var tempDef = definitions["temperature"];
            Assert.IsNotNull(tempDef);
            Assert.AreEqual(DataTypeEnum.Double, tempDef.DataType);
            Assert.AreEqual("°C", tempDef.Unit);

            // 验证嵌套传感器值地址定义
            Assert.IsTrue(definitions.ContainsKey("device.sensor.value"));
            var sensorDef = definitions["device.sensor.value"];
            Assert.IsNotNull(sensorDef);
            Assert.AreEqual(DataTypeEnum.Double, sensorDef.DataType);
            Assert.AreEqual("unit", sensorDef.Unit);

            // 验证数组访问地址定义
            Assert.IsTrue(definitions.ContainsKey("sensors[0].value"));
            var arrayDef = definitions["sensors[0].value"];
            Assert.IsNotNull(arrayDef);
            Assert.AreEqual(DataTypeEnum.Double, arrayDef.DataType);
            Assert.AreEqual("unit", arrayDef.Unit);
        }

        [TestMethod]
        public void TestJsonValueExtraction_SimpleValue()
        {
            // Arrange
            var json = JToken.Parse(@"{
                ""temperature"": 25.5,
                ""humidity"": 60
            }");

            // Act
            var value = _client.GetJsonValue(json, "temperature");

            // Assert
            Assert.IsNotNull(value);
            Assert.AreEqual(25.5, value.Value<double>());
        }

        [TestMethod]
        public void TestJsonValueExtraction_NestedValue()
        {
            // Arrange
            var json = JToken.Parse(@"{
                ""device"": {
                    ""sensor"": {
                        ""value"": 42.1
                    }
                }
            }");

            // Act
            var value = _client.GetJsonValue(json, "device.sensor.value");

            // Assert
            Assert.IsNotNull(value);
            Assert.AreEqual(42.1, value.Value<double>());
        }

        [TestMethod]
        public void TestJsonValueExtraction_ArrayValue()
        {
            // Arrange
            var json = JToken.Parse(@"{
                ""sensors"": [
                    { ""value"": 1.1 },
                    { ""value"": 2.2 }
                ]
            }");

            // Act
            var value = _client.GetJsonValue(json, "sensors[0].value");

            // Assert
            Assert.IsNotNull(value);
            Assert.AreEqual(1.1, value.Value<double>());
        }

        [TestMethod]
        public void TestRead_ShouldReturnGoodStatusForValidData()
        {
            // Arrange
            var json = JToken.Parse(@"{
                ""temperature"": 25.5,
                ""humidity"": 60
            }");
            var field = typeof(DeviceWebSocketClient).GetField("_lastReceivedData", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(_client, json);

            // Act
            var result = _client.Read(new DriverAddressIoArgModel { Address = "temperature" });

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(VaribaleStatusTypeEnum.Good, result.StatusType);
            Assert.AreEqual(25.5, Convert.ToDouble(result.Value));
        }

        [TestMethod]
        public void TestRead_ShouldReturnBadStatusForInvalidData()
        {
            // Arrange
            var json = JToken.Parse(@"{
                ""temperature"": 25.5,
                ""humidity"": 60
            }");
            var field = typeof(DeviceWebSocketClient).GetField("_lastReceivedData", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(_client, json);

            // Act
            var result = _client.Read(new DriverAddressIoArgModel { Address = "nonexistent" });

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(VaribaleStatusTypeEnum.Bad, result.StatusType);
            Assert.IsNotNull(result.Message);
        }

        [TestMethod]
        public void TestWrite_ShouldReturnSuccessResponse()
        {
            // Arrange
            _client.Connect();
            Assert.IsTrue(_clientConnectedEvent.Wait(TimeSpan.FromSeconds(5)), "Client should connect");

            // Act
            var result = _client.WriteAsync("request1", "setValue", 
                new DriverAddressIoArgModel { Address = "temperature", Value = 25.5 }).Result;

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual("request1", result.RequestId);
            Assert.AreEqual("setValue", result.Method);
        }

        [TestMethod]
        public void TestWrite_ShouldReturnFailureResponseWhenDisconnected()
        {
            // Act
            var result = _client.WriteAsync("request1", "setValue", 
                new DriverAddressIoArgModel { Address = "temperature", Value = 25.5 }).Result;

            // Assert
            Assert.IsNotNull(result);
            Assert.IsFalse(result.IsSuccess);
            Assert.IsNotNull(result.Description);
            Assert.AreEqual("request1", result.RequestId);
            Assert.AreEqual("setValue", result.Method);
        }

        #endregion
    }
}
