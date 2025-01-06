using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using TCP.DKScrew;
using TCP.DKScrew.Models;
using Newtonsoft.Json;
using PluginInterface;

namespace TCP.DKScrew.Tests
{
    public class DKScrewDriverTests
    {
        private readonly Mock<ILogger<DKScrewPlugin>> _loggerMock;
        private readonly DKScrewPlugin _plugin;

        public DKScrewDriverTests()
        {
            _loggerMock = new Mock<ILogger<DKScrewPlugin>>();
            _plugin = new DKScrewPlugin(_loggerMock.Object);
        }

        [Fact]
        public void Configure_ValidConfig_ShouldNotThrow()
        {
            // Arrange
            var config = new DKScrewDeviceConfig
            {
                IpAddress = "127.0.0.1",
                Port = 8080
            };
            var configJson = JsonConvert.SerializeObject(config);

            // Act & Assert
            var exception = Record.Exception(() => _plugin.Configure(configJson));
            Assert.Null(exception);
        }

        [Fact]
        public async Task Connect_ValidConfig_ShouldReturnFalse()
        {
            // Arrange
            var config = new DKScrewDeviceConfig
            {
                IpAddress = "127.0.0.1",
                Port = 8080
            };
            var configJson = JsonConvert.SerializeObject(config);
            _plugin.Configure(configJson);

            // Act
            var result = await _plugin.ConnectAsync();

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task ReadNode_NotConnected_ShouldReturnBadStatus()
        {
            // Arrange
            var nodeId = "test";

            // Act
            var result = await _plugin.ReadNode(nodeId);

            // Assert
            Assert.Equal(VaribaleStatusTypeEnum.Bad, ((DriverReturnValueModel)result).StatusType);
        }

        [Fact]
        public async Task WriteNode_NotConnected_ShouldReturnFalse()
        {
            // Arrange
            var nodeId = "test";
            var value = "test";

            // Act
            var result = await _plugin.WriteNode(nodeId, value);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task Disconnect_ShouldReturnTrue()
        {
            // Act
            var result = await _plugin.DisconnectAsync();

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void Dispose_ShouldNotThrow()
        {
            // Act & Assert
            var exception = Record.Exception(() => _plugin.Dispose());
            Assert.Null(exception);
        }
    }
}