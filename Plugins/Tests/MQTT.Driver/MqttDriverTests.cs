using System;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using PluginInterface;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Generic;

namespace MQTT.Driver.Tests
{
    public class MqttDriverTests
    {
        private readonly MqttDriver _driver;
        private readonly Dictionary<string, MqttSubscription> _subscriptions;

        public MqttDriverTests()
        {
            var logger = NullLogger<MqttDriver>.Instance;
            _driver = new MqttDriver("TestDevice", logger);
            _subscriptions = new Dictionary<string, MqttSubscription>();

            // Use reflection to access private fields
            var subscriptionsField = typeof(MqttDriver).GetField("_subscriptions", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            subscriptionsField?.SetValue(_driver, _subscriptions);

            var isConnectedField = typeof(MqttDriver).GetField("_isConnected",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            isConnectedField?.SetValue(_driver, true);
        }

        [Fact]
        public void Configure_ValidConfig_ShouldNotThrow()
        {
            // Arrange & Act & Assert
            _driver.BrokerAddress = "test.mosquitto.org";
            _driver.Port = 1883;
            _driver.Username = "testuser";
            _driver.Password = "testpass";
            _driver.ClientId = "testclient";
            _driver.Timeout = 5000;
            _driver.MinPeriod = 1000;

            // No exception should be thrown
        }

        [Fact]
        public void Read_WithValidSubscription_ReturnsCorrectValue()
        {
            // Arrange
            var topic = "sensor/temperature";
            var jsonData = @"{
                ""value"": 23.5,
                ""timestamp"": ""2025-01-06T14:37:36+08:00"",
                ""quality"": ""GOOD""
            }";
            
            var jsonElement = JsonSerializer.Deserialize<JsonElement>(jsonData);
            _subscriptions[topic] = new MqttSubscription { LastValue = jsonElement };

            var ioArg = new DriverAddressIoArgModel
            {
                Address = "sensor/temperature.value",
                ValueType = DataTypeEnum.Float
            };

            // Act
            var result = _driver.Read(ioArg);

            // Assert
            Assert.Equal(VaribaleStatusTypeEnum.Good, result.StatusType);
            Assert.Equal(23.5, result.Value);
        }

        [Fact]
        public void Read_WithInvalidTopic_ReturnsBadStatus()
        {
            // Arrange
            var ioArg = new DriverAddressIoArgModel
            {
                Address = "nonexistent/topic.value",
                ValueType = DataTypeEnum.Float
            };

            // Act
            var result = _driver.Read(ioArg);

            // Assert
            Assert.Equal(VaribaleStatusTypeEnum.Bad, result.StatusType);
            Assert.Contains("No subscription found for topic", result.Message);
        }

        [Fact]
        public void Read_WithInvalidJsonPath_ReturnsBadStatus()
        {
            // Arrange
            var topic = "sensor/temperature";
            var jsonData = @"{
                ""value"": 23.5
            }";
            
            var jsonElement = JsonSerializer.Deserialize<JsonElement>(jsonData);
            _subscriptions[topic] = new MqttSubscription { LastValue = jsonElement };

            var ioArg = new DriverAddressIoArgModel
            {
                Address = "sensor/temperature.nonexistent",
                ValueType = DataTypeEnum.Float
            };

            // Act
            var result = _driver.Read(ioArg);

            // Assert
            Assert.Equal(VaribaleStatusTypeEnum.Bad, result.StatusType);
            Assert.Contains("JSON path", result.Message);
        }

        [Fact]
        public void Read_WithDifferentDataTypes_ReturnsCorrectValues()
        {
            // Arrange
            var topic = "sensor/data";
            var jsonData = @"{
                ""intValue"": 42,
                ""floatValue"": 3.14,
                ""boolValue"": true,
                ""stringValue"": ""test"",
                ""nullValue"": null
            }";
            
            var jsonElement = JsonSerializer.Deserialize<JsonElement>(jsonData);
            _subscriptions[topic] = new MqttSubscription { LastValue = jsonElement };

            // Test integer
            var intResult = _driver.Read(new DriverAddressIoArgModel
            {
                Address = "sensor/data.intValue",
                ValueType = DataTypeEnum.Int32
            });
            Assert.Equal(42, intResult.Value);

            // Test float
            var floatResult = _driver.Read(new DriverAddressIoArgModel
            {
                Address = "sensor/data.floatValue",
                ValueType = DataTypeEnum.Float
            });
            Assert.Equal(3.14, floatResult.Value);

            // Test boolean
            var boolResult = _driver.Read(new DriverAddressIoArgModel
            {
                Address = "sensor/data.boolValue",
                ValueType = DataTypeEnum.Boolean
            });
            Assert.Equal(true, boolResult.Value);

            // Test string
            var stringResult = _driver.Read(new DriverAddressIoArgModel
            {
                Address = "sensor/data.stringValue",
                ValueType = DataTypeEnum.String
            });
            Assert.Equal("test", stringResult.Value);

            // Test null
            var nullResult = _driver.Read(new DriverAddressIoArgModel
            {
                Address = "sensor/data.nullValue",
                ValueType = DataTypeEnum.String
            });
            Assert.Null(nullResult.Value);
        }

        [Fact]
        public void Read_WhenNotConnected_ReturnsBadStatus()
        {
            // Arrange
            var isConnectedField = typeof(MqttDriver).GetField("_isConnected",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            isConnectedField?.SetValue(_driver, false);

            var ioArg = new DriverAddressIoArgModel
            {
                Address = "sensor/temperature.value",
                ValueType = DataTypeEnum.Float
            };

            // Act
            var result = _driver.Read(ioArg);

            // Assert
            Assert.Equal(VaribaleStatusTypeEnum.Bad, result.StatusType);
            Assert.Contains("Not connected", result.Message);
        }

        [Fact]
        public void Read_WithInvalidAddressFormat_ReturnsBadStatus()
        {
            // Arrange
            var ioArg = new DriverAddressIoArgModel
            {
                Address = "invalidaddressformat",
                ValueType = DataTypeEnum.Float
            };

            // Act
            var result = _driver.Read(ioArg);

            // Assert
            Assert.Equal(VaribaleStatusTypeEnum.Bad, result.StatusType);
            Assert.Contains("Invalid address format", result.Message);
        }

        [Fact]
        public async Task WriteAsync_WhenConnected_ReturnsSuccess()
        {
            // Arrange
            var ioArg = new DriverAddressIoArgModel
            {
                Address = "test/topic",
                Value = "test value"
            };

            // Act
            var result = await _driver.WriteAsync("123", "test", ioArg);

            // Assert
            Assert.True(result.IsSuccess);
        }

        [Fact]
        public async Task WriteAsync_WhenNotConnected_ReturnsFalse()
        {
            // Arrange
            var isConnectedField = typeof(MqttDriver).GetField("_isConnected",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            isConnectedField?.SetValue(_driver, false);

            var ioArg = new DriverAddressIoArgModel
            {
                Address = "test/topic",
                Value = "test value"
            };

            // Act
            var result = await _driver.WriteAsync("123", "test", ioArg);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Contains("Not connected", result.Description);
        }
    }
}
