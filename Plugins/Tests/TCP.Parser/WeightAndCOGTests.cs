using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using System.Text;
using Xunit;
using PluginInterface;

namespace TCP.Parser.Tests
{
    public class WeightAndCOGTests
    {
        private readonly Mock<ILogger> _loggerMock;
        private readonly TCPParser _parser;

        public WeightAndCOGTests()
        {
            _loggerMock = new Mock<ILogger>();
            _parser = new TCPParser("WeightAndCOGTest", _loggerMock.Object)
            {
                IpAddress = "127.0.0.1",
                Port = 12345,
                ProtocolType = ProtocolTypeEnum.TCP
            };
        }

        [Fact]
        public void DeviceStatus_ParsesCorrectly()
        {
            // Arrange
            var statusData = new
            {
                D_STATES = 21,
                NON_FAULT = 1,
                SYS_PLC_FAULT = 0,
                SYS_SERVO_FAULT = 0,
                DEVICES_ESTOP = 0,
                DEVICES_TESTMODE = 0,
                DEVICES_SWITHUP = 0,
                DEVICES_SWITHDOWN = 0
            };
            string jsonData = JsonConvert.SerializeObject(statusData);
            byte[] data = Encoding.UTF8.GetBytes(jsonData);
            _parser.SetTestData(data);

            // Act
            var deviceState = _parser.TestRead(new DriverAddressIoArgModel { Address = "json:D_STATES", ValueType = DataTypeEnum.Int32 });
            var nonFault = _parser.TestRead(new DriverAddressIoArgModel { Address = "json:NON_FAULT", ValueType = DataTypeEnum.Int32 });
            var plcFault = _parser.TestRead(new DriverAddressIoArgModel { Address = "json:SYS_PLC_FAULT", ValueType = DataTypeEnum.Int32 });
            var servoFault = _parser.TestRead(new DriverAddressIoArgModel { Address = "json:SYS_SERVO_FAULT", ValueType = DataTypeEnum.Int32 });
            var eStop = _parser.TestRead(new DriverAddressIoArgModel { Address = "json:DEVICES_ESTOP", ValueType = DataTypeEnum.Int32 });
            var testMode = _parser.TestRead(new DriverAddressIoArgModel { Address = "json:DEVICES_TESTMODE", ValueType = DataTypeEnum.Int32 });
            var switchUp = _parser.TestRead(new DriverAddressIoArgModel { Address = "json:DEVICES_SWITHUP", ValueType = DataTypeEnum.Int32 });
            var switchDown = _parser.TestRead(new DriverAddressIoArgModel { Address = "json:DEVICES_SWITHDOWN", ValueType = DataTypeEnum.Int32 });

            // Assert
            Assert.Equal(VaribaleStatusTypeEnum.Good, deviceState.StatusType);
            Assert.Equal(21, Convert.ToInt32(deviceState.Value));
            Assert.Equal(VaribaleStatusTypeEnum.Good, nonFault.StatusType);
            Assert.Equal(1, Convert.ToInt32(nonFault.Value));
            Assert.Equal(VaribaleStatusTypeEnum.Good, plcFault.StatusType);
            Assert.Equal(0, Convert.ToInt32(plcFault.Value));
            Assert.Equal(VaribaleStatusTypeEnum.Good, servoFault.StatusType);
            Assert.Equal(0, Convert.ToInt32(servoFault.Value));
            Assert.Equal(VaribaleStatusTypeEnum.Good, eStop.StatusType);
            Assert.Equal(0, Convert.ToInt32(eStop.Value));
            Assert.Equal(VaribaleStatusTypeEnum.Good, testMode.StatusType);
            Assert.Equal(0, Convert.ToInt32(testMode.Value));
            Assert.Equal(VaribaleStatusTypeEnum.Good, switchUp.StatusType);
            Assert.Equal(0, Convert.ToInt32(switchUp.Value));
            Assert.Equal(VaribaleStatusTypeEnum.Good, switchDown.StatusType);
            Assert.Equal(0, Convert.ToInt32(switchDown.Value));
        }

        [Fact]
        public void TestResult_ParsesCorrectly()
        {
            // Arrange
            var resultData = new
            {
                PRODUCT_TYPE = "MPTA-5000",
                PRODUCT_NUMBER = "0145678",
                PRODUCT_STATE = "转一圈测量",
                TEST_MASS = 2234.8,
                TEST_LENG_X = 2345.9,
                TEST_LENG_Y = 0.0,
                TEST_LENG_Z = 0.0,
                TEST_RO = 0.0,
                TEST_SITA = 3.3,
                TEST_RESULT = "合格"
            };
            string jsonData = JsonConvert.SerializeObject(resultData);
            byte[] data = Encoding.UTF8.GetBytes(jsonData);
            _parser.SetTestData(data);

            // Act
            var productType = _parser.TestRead(new DriverAddressIoArgModel { Address = "json:PRODUCT_TYPE", ValueType = DataTypeEnum.AsciiString });
            var productNumber = _parser.TestRead(new DriverAddressIoArgModel { Address = "json:PRODUCT_NUMBER", ValueType = DataTypeEnum.AsciiString });
            var productState = _parser.TestRead(new DriverAddressIoArgModel { Address = "json:PRODUCT_STATE", ValueType = DataTypeEnum.AsciiString });
            var testMass = _parser.TestRead(new DriverAddressIoArgModel { Address = "json:TEST_MASS", ValueType = DataTypeEnum.Double });
            var testLengX = _parser.TestRead(new DriverAddressIoArgModel { Address = "json:TEST_LENG_X", ValueType = DataTypeEnum.Double });
            var testLengY = _parser.TestRead(new DriverAddressIoArgModel { Address = "json:TEST_LENG_Y", ValueType = DataTypeEnum.Double });
            var testLengZ = _parser.TestRead(new DriverAddressIoArgModel { Address = "json:TEST_LENG_Z", ValueType = DataTypeEnum.Double });
            var testRo = _parser.TestRead(new DriverAddressIoArgModel { Address = "json:TEST_RO", ValueType = DataTypeEnum.Double });
            var testSita = _parser.TestRead(new DriverAddressIoArgModel { Address = "json:TEST_SITA", ValueType = DataTypeEnum.Double });
            var testResult = _parser.TestRead(new DriverAddressIoArgModel { Address = "json:TEST_RESULT", ValueType = DataTypeEnum.AsciiString });

            // Assert
            Assert.Equal(VaribaleStatusTypeEnum.Good, productType.StatusType);
            Assert.Equal("MPTA-5000", productType.Value);
            Assert.Equal(VaribaleStatusTypeEnum.Good, productNumber.StatusType);
            Assert.Equal("0145678", productNumber.Value);
            Assert.Equal(VaribaleStatusTypeEnum.Good, productState.StatusType);
            Assert.Equal("转一圈测量", productState.Value);
            Assert.Equal(VaribaleStatusTypeEnum.Good, testMass.StatusType);
            Assert.Equal(2234.8, Convert.ToDouble(testMass.Value));
            Assert.Equal(VaribaleStatusTypeEnum.Good, testLengX.StatusType);
            Assert.Equal(2345.9, Convert.ToDouble(testLengX.Value));
            Assert.Equal(VaribaleStatusTypeEnum.Good, testLengY.StatusType);
            Assert.Equal(0.0, Convert.ToDouble(testLengY.Value));
            Assert.Equal(VaribaleStatusTypeEnum.Good, testLengZ.StatusType);
            Assert.Equal(0.0, Convert.ToDouble(testLengZ.Value));
            Assert.Equal(VaribaleStatusTypeEnum.Good, testRo.StatusType);
            Assert.Equal(0.0, Convert.ToDouble(testRo.Value));
            Assert.Equal(VaribaleStatusTypeEnum.Good, testSita.StatusType);
            Assert.Equal(3.3, Convert.ToDouble(testSita.Value));
            Assert.Equal(VaribaleStatusTypeEnum.Good, testResult.StatusType);
            Assert.Equal("合格", testResult.Value);
        }

        [Theory]
        [InlineData(20, "设备空闲、大量程、可测试")]
        [InlineData(21, "设备空闲、小量程、可测试")]
        [InlineData(22, "设备不可测试状态")]
        [InlineData(23, "设备测试中、台面静止")]
        [InlineData(24, "设备测试中、台面上升")]
        [InlineData(25, "设备测试中、台面旋转")]
        [InlineData(26, "设备测试中、台面下降")]
        [InlineData(27, "设备测试完成")]
        public void DeviceState_DescriptionIsCorrect(int state, string expectedDescription)
        {
            // Arrange
            var statusData = new { D_STATES = state, D_STATES_DESC = expectedDescription };
            string jsonData = JsonConvert.SerializeObject(statusData);
            byte[] data = Encoding.UTF8.GetBytes(jsonData);
            _parser.SetTestData(data);

            // Act
            var deviceState = _parser.TestRead(new DriverAddressIoArgModel { Address = "json:D_STATES", ValueType = DataTypeEnum.Int32 });
            var stateDescription = _parser.TestRead(new DriverAddressIoArgModel { Address = "json:D_STATES_DESC", ValueType = DataTypeEnum.AsciiString });

            // Assert
            Assert.Equal(VaribaleStatusTypeEnum.Good, deviceState.StatusType);
            Assert.Equal(state, Convert.ToInt32(deviceState.Value));
            Assert.Equal(VaribaleStatusTypeEnum.Good, stateDescription.StatusType);
            Assert.Equal(expectedDescription, stateDescription.Value);
        }

        [Theory]
        [InlineData(1, 0, 0, 0, 0, 0, 0, true)]  // All normal
        [InlineData(0, 0, 0, 0, 0, 0, 0, false)] // Device not normal
        [InlineData(1, 1, 0, 0, 0, 0, 0, false)] // PLC fault
        [InlineData(1, 0, 1, 0, 0, 0, 0, false)] // Servo fault
        [InlineData(1, 0, 0, 1, 0, 0, 0, false)] // Emergency stop
        [InlineData(1, 0, 0, 0, 1, 0, 0, false)] // Test mode fault
        [InlineData(1, 0, 0, 0, 0, 1, 0, false)] // Switch up fault
        [InlineData(1, 0, 0, 0, 0, 0, 1, false)] // Switch down fault
        public void DeviceStatus_FaultDetection(
            int nonFault, int plcFault, int servoFault, int eStop,
            int testMode, int switchUp, int switchDown, bool expectedNormal)
        {
            // Arrange
            var statusData = new
            {
                NON_FAULT = nonFault,
                SYS_PLC_FAULT = plcFault,
                SYS_SERVO_FAULT = servoFault,
                DEVICES_ESTOP = eStop,
                DEVICES_TESTMODE = testMode,
                DEVICES_SWITHUP = switchUp,
                DEVICES_SWITHDOWN = switchDown
            };
            string jsonData = JsonConvert.SerializeObject(statusData);
            byte[] data = Encoding.UTF8.GetBytes(jsonData);
            _parser.SetTestData(data);

            // Act
            var deviceNormalState = _parser.TestRead(new DriverAddressIoArgModel { Address = "json:DEVICE_NORMAL", ValueType = DataTypeEnum.Bool });

            // Assert
            Assert.Equal(VaribaleStatusTypeEnum.Good, deviceNormalState.StatusType);
            Assert.Equal(expectedNormal, Convert.ToBoolean(deviceNormalState.Value));
        }

        [Theory]
        [InlineData(-1000.0, -1000.0, -1000.0, -360.0, -360.0, "不合格")] // Lower bounds
        [InlineData(0.0, 0.0, 0.0, 0.0, 0.0, "合格")] // Zero values
        [InlineData(10000.0, 1000.0, 1000.0, 360.0, 360.0, "合格")] // Upper bounds
        public void TestResult_BoundaryValues(
            double mass, double lengX, double lengY,
            double ro, double sita, string result)
        {
            // Arrange
            var resultData = new
            {
                PRODUCT_TYPE = "MPTA-5000",
                PRODUCT_NUMBER = "0145678",
                PRODUCT_STATE = "转一圈测量",
                TEST_MASS = mass,
                TEST_LENG_X = lengX,
                TEST_LENG_Y = lengY,
                TEST_LENG_Z = 0.0,
                TEST_RO = ro,
                TEST_SITA = sita,
                TEST_RESULT = result
            };
            string jsonData = JsonConvert.SerializeObject(resultData);
            byte[] data = Encoding.UTF8.GetBytes(jsonData);
            _parser.SetTestData(data);

            // Act
            var testMass = _parser.TestRead(new DriverAddressIoArgModel { Address = "json:TEST_MASS", ValueType = DataTypeEnum.Double });
            var testLengX = _parser.TestRead(new DriverAddressIoArgModel { Address = "json:TEST_LENG_X", ValueType = DataTypeEnum.Double });
            var testLengY = _parser.TestRead(new DriverAddressIoArgModel { Address = "json:TEST_LENG_Y", ValueType = DataTypeEnum.Double });
            var testRo = _parser.TestRead(new DriverAddressIoArgModel { Address = "json:TEST_RO", ValueType = DataTypeEnum.Double });
            var testSita = _parser.TestRead(new DriverAddressIoArgModel { Address = "json:TEST_SITA", ValueType = DataTypeEnum.Double });
            var testResult = _parser.TestRead(new DriverAddressIoArgModel { Address = "json:TEST_RESULT", ValueType = DataTypeEnum.AsciiString });

            // Assert
            Assert.Equal(VaribaleStatusTypeEnum.Good, testMass.StatusType);
            Assert.Equal(mass, Convert.ToDouble(testMass.Value));
            Assert.Equal(VaribaleStatusTypeEnum.Good, testLengX.StatusType);
            Assert.Equal(lengX, Convert.ToDouble(testLengX.Value));
            Assert.Equal(VaribaleStatusTypeEnum.Good, testLengY.StatusType);
            Assert.Equal(lengY, Convert.ToDouble(testLengY.Value));
            Assert.Equal(VaribaleStatusTypeEnum.Good, testRo.StatusType);
            Assert.Equal(ro, Convert.ToDouble(testRo.Value));
            Assert.Equal(VaribaleStatusTypeEnum.Good, testSita.StatusType);
            Assert.Equal(sita, Convert.ToDouble(testSita.Value));
            Assert.Equal(VaribaleStatusTypeEnum.Good, testResult.StatusType);
            Assert.Equal(result, testResult.Value);
        }

        [Fact]
        public void DeviceCommand_Construction()
        {
            // Arrange
            var productType = "MPTA-5000";
            var productNumber = "0145678";
            var productState = "转一圈测量";
            var expectedCommand = $"MPTA-5000:2:\"{productType}\":\"{productNumber}\":\"{productState}\"";

            // Act
            var command = _parser.TestRead(new DriverAddressIoArgModel 
            { 
                Address = $"cmd:START_TEST:{productType}:{productNumber}:{productState}", 
                ValueType = DataTypeEnum.AsciiString 
            });

            // Assert
            Assert.Equal(VaribaleStatusTypeEnum.Good, command.StatusType);
            Assert.Equal(expectedCommand, command.Value);
        }

        [Fact]
        public void InvalidJson_ReturnsError()
        {
            // Arrange
            string invalidJson = "{ invalid json }";
            byte[] data = Encoding.UTF8.GetBytes(invalidJson);
            _parser.SetTestData(data);

            // Act
            var result = _parser.TestRead(new DriverAddressIoArgModel { Address = "json:D_STATES", ValueType = DataTypeEnum.Int32 });

            // Assert
            Assert.Equal(VaribaleStatusTypeEnum.Bad, result.StatusType);
        }

        [Fact]
        public void NonexistentField_ReturnsError()
        {
            // Arrange
            var statusData = new { D_STATES = 21 };
            string jsonData = JsonConvert.SerializeObject(statusData);
            byte[] data = Encoding.UTF8.GetBytes(jsonData);
            _parser.SetTestData(data);

            // Act
            var result = _parser.TestRead(new DriverAddressIoArgModel { Address = "json:NONEXISTENT_FIELD", ValueType = DataTypeEnum.Int32 });

            // Assert
            Assert.Equal(VaribaleStatusTypeEnum.Bad, result.StatusType);
        }
    }
}
