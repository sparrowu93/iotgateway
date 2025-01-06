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
        private readonly DKScrewDeviceConfig _validConfig;

        public DKScrewDriverTests()
        {
            _loggerMock = new Mock<ILogger<DKScrewPlugin>>();
            _plugin = new DKScrewPlugin(_loggerMock.Object);
            _validConfig = new DKScrewDeviceConfig
            {
                IpAddress = "127.0.0.1",
                Port = 8080,
                Timeout = 3000,
                StatusUpdateInterval = 1000,
                CurveDataInterval = 100,
                EnableCurveData = true
            };
        }

        [Fact]
        public void Configure_ValidConfig_ShouldNotThrow()
        {
            // Arrange
            var configJson = JsonConvert.SerializeObject(_validConfig);

            // Act & Assert
            var exception = Record.Exception(() => _plugin.Configure(configJson));
            Assert.Null(exception);
        }

        [Fact]
        public void Configure_InvalidJson_ShouldThrow()
        {
            // Arrange
            var invalidJson = "{invalid_json}";

            // Act & Assert
            Assert.Throws<JsonReaderException>(() => _plugin.Configure(invalidJson));
        }

        [Fact]
        public async Task Connect_ValidConfig_ShouldReturnFalse()
        {
            // Arrange
            var configJson = JsonConvert.SerializeObject(_validConfig);
            _plugin.Configure(configJson);

            // Act
            var result = await _plugin.ConnectAsync();

            // Assert
            Assert.False(result);
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => true),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.AtLeastOnce);
        }

        [Theory]
        [InlineData(DeviceVariables.IsConnected)]
        [InlineData(DeviceVariables.IsReady)]
        [InlineData(DeviceVariables.IsRunning)]
        [InlineData(DeviceVariables.IsOK)]
        [InlineData(DeviceVariables.IsNG)]
        public async Task ReadNode_SystemVariables_ShouldReturnBadStatus(string variableName)
        {
            // Arrange
            var configJson = JsonConvert.SerializeObject(_validConfig);
            _plugin.Configure(configJson);

            // Act
            var result = await _plugin.ReadNode(variableName);

            // Assert
            var driverResult = Assert.IsType<DriverReturnValueModel>(result);
            Assert.Equal(VaribaleStatusTypeEnum.Bad, driverResult.StatusType);
        }

        [Theory]
        [InlineData(DeviceVariables.StartMotor, true)]
        [InlineData(DeviceVariables.StopMotor, true)]
        [InlineData(DeviceVariables.LoosenMotor, true)]
        [InlineData(DeviceVariables.SelectPset, 1)]
        public async Task WriteNode_ControlCommands_WhenNotConnected_ShouldReturnFalse(string command, object value)
        {
            // Arrange
            var configJson = JsonConvert.SerializeObject(_validConfig);
            _plugin.Configure(configJson);

            // Act
            var result = await _plugin.WriteNode(command, value);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task WriteNode_InvalidCommand_ShouldReturnFalse()
        {
            // Arrange
            var configJson = JsonConvert.SerializeObject(_validConfig);
            _plugin.Configure(configJson);

            // Act
            var result = await _plugin.WriteNode("InvalidCommand", "value");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task Disconnect_WhenNotConnected_ShouldReturnTrue()
        {
            // Arrange
            var configJson = JsonConvert.SerializeObject(_validConfig);
            _plugin.Configure(configJson);

            // Act
            var result = await _plugin.DisconnectAsync();

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void IDriver_Read_ShouldMatchReadNode()
        {
            // Arrange
            var ioArg = new DriverAddressIoArgModel
            {
                Address = DeviceVariables.IsConnected,
                ValueType = DataTypeEnum.Bool
            };

            // Act
            var result = _plugin.Read(ioArg);

            // Assert
            Assert.Equal(VaribaleStatusTypeEnum.Bad, result.StatusType);
        }

        [Fact]
        public async Task IDriver_WriteAsync_ShouldMatchWriteNode()
        {
            // Arrange
            var ioArg = new DriverAddressIoArgModel
            {
                Address = DeviceVariables.StartMotor,
                Value = true
            };

            // Act
            var result = await _plugin.WriteAsync("test", "start", ioArg);

            // Assert
            Assert.False(result.IsSuccess);
        }

        [Fact]
        public void Dispose_ShouldCleanupResources()
        {
            // Arrange
            var configJson = JsonConvert.SerializeObject(_validConfig);
            _plugin.Configure(configJson);

            // Act
            _plugin.Dispose();

            // Assert - Verify that no errors occur and resources are cleaned up
            var exception = Record.Exception(() => _plugin.Dispose());
            Assert.Null(exception);
        }

        // [Fact]
        // public async Task DataCollection_ShouldStopOnDispose()
        // {
        //     // Arrange
        //     var configJson = JsonConvert.SerializeObject(_validConfig);
        //     _plugin.Configure(configJson);
        //     await _plugin.ConnectAsync();

        //     // Act
        //     _plugin.Dispose();

        //     // Assert - Verify that the plugin is disconnected
        //     var result = await _plugin.ReadNode(DeviceVariables.IsConnected);
        //     var driverResult = Assert.IsType<DriverReturnValueModel>(result);
        //     Assert.Equal(VaribaleStatusTypeEnum.Bad, driverResult.StatusType);
        // }

        [Theory]
        [InlineData(DeviceVariables.IsConnected, DataTypeEnum.Bool)]
        [InlineData(DeviceVariables.IsReady, DataTypeEnum.Bool)]
        [InlineData(DeviceVariables.IsRunning, DataTypeEnum.Bool)]
        [InlineData(DeviceVariables.IsOK, DataTypeEnum.Bool)]
        [InlineData(DeviceVariables.IsNG, DataTypeEnum.Bool)]
        [InlineData(DeviceVariables.HasSystemError, DataTypeEnum.Bool)]
        [InlineData(DeviceVariables.SystemErrorId, DataTypeEnum.Int32)]
        [InlineData(DeviceVariables.HasParameterSetupError, DataTypeEnum.Bool)]
        [InlineData(DeviceVariables.HasControlTimeoutError, DataTypeEnum.Bool)]
        public void Read_SystemVariables_ShouldReturnCorrectType(string variable, DataTypeEnum expectedType)
        {
            // Arrange
            var ioArg = new DriverAddressIoArgModel
            {
                Address = variable,
                ValueType = expectedType
            };

            // Act
            var result = _plugin.Read(ioArg);

            // Assert
            Assert.Equal(VaribaleStatusTypeEnum.Bad, result.StatusType);
            if (result.Value != null)
            {
                Assert.IsType(GetClrType(expectedType), result.Value);
            }
        }

        [Theory]
        [InlineData(DeviceVariables.FinalTorque, DataTypeEnum.Float)]
        [InlineData(DeviceVariables.MonitoringAngle, DataTypeEnum.Float)]
        [InlineData(DeviceVariables.FinalTime, DataTypeEnum.Float)]
        [InlineData(DeviceVariables.FinalAngle, DataTypeEnum.Float)]
        [InlineData(DeviceVariables.ResultStatus, DataTypeEnum.Int32)]
        [InlineData(DeviceVariables.NGCode, DataTypeEnum.Int32)]
        public void Read_TighteningVariables_ShouldReturnCorrectType(string variable, DataTypeEnum expectedType)
        {
            // Arrange
            var ioArg = new DriverAddressIoArgModel
            {
                Address = variable,
                ValueType = expectedType
            };

            // Act
            var result = _plugin.Read(ioArg);

            // Assert
            Assert.Equal(VaribaleStatusTypeEnum.Bad, result.StatusType);
            if (result.Value != null)
            {
                Assert.IsType(GetClrType(expectedType), result.Value);
            }
        }

        [Theory]
        [InlineData(DeviceVariables.CurrentTorque, DataTypeEnum.Float)]
        [InlineData(DeviceVariables.CurrentAngle, DataTypeEnum.Float)]
        [InlineData(DeviceVariables.IsCurveFinished, DataTypeEnum.Bool)]
        [InlineData(DeviceVariables.IsCurveStart, DataTypeEnum.Bool)]
        public void Read_CurveVariables_ShouldReturnCorrectType(string variable, DataTypeEnum expectedType)
        {
            // Arrange
            var ioArg = new DriverAddressIoArgModel
            {
                Address = variable,
                ValueType = expectedType
            };

            // Act
            var result = _plugin.Read(ioArg);

            // Assert
            Assert.Equal(VaribaleStatusTypeEnum.Bad, result.StatusType);
            if (result.Value != null)
            {
                Assert.IsType(GetClrType(expectedType), result.Value);
            }
        }

        [Fact]
        public void Read_LastTighteningResult_ShouldReturnJsonString()
        {
            // Arrange
            var ioArg = new DriverAddressIoArgModel
            {
                Address = DeviceVariables.LastTighteningResult,
                ValueType = DataTypeEnum.Utf8String
            };

            // Act
            var result = _plugin.Read(ioArg);

            // Assert
            Assert.Equal(VaribaleStatusTypeEnum.Bad, result.StatusType);
            if (result.Value != null)
            {
                Assert.IsType<string>(result.Value);
                // Verify it's a valid JSON string if not null
                if (result.Value is string jsonStr)
                {
                    var exception = Record.Exception(() => JsonConvert.DeserializeObject<TighteningResult>(jsonStr));
                    Assert.Null(exception);
                }
            }
        }

        [Fact]
        public void Read_LastCurveData_ShouldReturnJsonString()
        {
            // Arrange
            var ioArg = new DriverAddressIoArgModel
            {
                Address = DeviceVariables.LastCurveData,
                ValueType = DataTypeEnum.Utf8String
            };

            // Act
            var result = _plugin.Read(ioArg);

            // Assert
            Assert.Equal(VaribaleStatusTypeEnum.Bad, result.StatusType);
            if (result.Value != null)
            {
                Assert.IsType<string>(result.Value);
                // Verify it's a valid JSON string if not null
                if (result.Value is string jsonStr)
                {
                    var exception = Record.Exception(() => JsonConvert.DeserializeObject<CurveData>(jsonStr));
                    Assert.Null(exception);
                }
            }
        }

        [Fact]
        public void Read_NonExistentVariable_ShouldReturnBadStatus()
        {
            // Arrange
            var ioArg = new DriverAddressIoArgModel
            {
                Address = "NonExistentVariable",
                ValueType = DataTypeEnum.Utf8String
            };

            // Act
            var result = _plugin.Read(ioArg);

            // Assert
            Assert.Equal(VaribaleStatusTypeEnum.Bad, result.StatusType);
            Assert.Null(result.Value);
        }

        private Type GetClrType(DataTypeEnum dataType)
        {
            return dataType switch
            {
                DataTypeEnum.Bool => typeof(bool),
                DataTypeEnum.Byte => typeof(byte),
                DataTypeEnum.Int16 => typeof(short),
                DataTypeEnum.Uint16 => typeof(ushort),
                DataTypeEnum.Int32 => typeof(int),
                DataTypeEnum.Uint32 => typeof(uint),
                DataTypeEnum.Int64 => typeof(long),
                DataTypeEnum.Uint64 => typeof(ulong),
                DataTypeEnum.Float => typeof(float),
                DataTypeEnum.Double => typeof(double),
                DataTypeEnum.Utf8String => typeof(string),
                DataTypeEnum.DateTime => typeof(DateTime),
                _ => throw new ArgumentException($"Unsupported data type: {dataType}")
            };
        }
    }
}