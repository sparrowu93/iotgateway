using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text;
using PluginInterface;
using Microsoft.Extensions.Logging;

namespace TCP.Parser.Tests
{
    [TestClass]
    public class TCPParserTests
    {
        private readonly string _deviceName;
        private readonly Microsoft.Extensions.Logging.ILogger _logger;

        public TCPParserTests()
        {
            _deviceName = "TCPParserTest";
            _logger = new ConsoleLogger();
        }

        [TestMethod]
        public void TestJsonParsing()
        {
            var parser = new TCPParser(_deviceName, _logger);
            
            // 设置测试数据
            string json = @"{""value"": 100.5, ""status"": true}";
            parser.SetTestData(Encoding.UTF8.GetBytes(json));

            // 测试数值读取
            var result = parser.TestRead(new DriverAddressIoArgModel 
            { 
                Address = "json:value",
                ValueType = DataTypeEnum.Double 
            });
            Assert.AreEqual(VaribaleStatusTypeEnum.Good, result.StatusType, $"Unexpected status: {result.Message}");
            Assert.AreEqual(100.5, Convert.ToDouble(result.Value));

            // 测试布尔值读取
            var statusResult = parser.TestRead(new DriverAddressIoArgModel 
            { 
                Address = "json:status",
                ValueType = DataTypeEnum.Bool 
            });
            Assert.AreEqual(VaribaleStatusTypeEnum.Good, statusResult.StatusType, $"Unexpected status: {statusResult.Message}");
            Assert.IsTrue(Convert.ToBoolean(statusResult.Value));
        }

        [TestMethod]
        public void TestNestedJsonParsing()
        {
            var parser = new TCPParser(_deviceName, _logger);
            
            // 设置测试数据
            string json = @"{
                ""device"": {
                    ""sensors"": [
                        {
                            ""value"": 100.5,
                            ""status"": true
                        }
                    ]
                }
            }";
            parser.SetTestData(Encoding.UTF8.GetBytes(json));

            // 测试嵌套JSON访问
            var result = parser.TestRead(new DriverAddressIoArgModel 
            { 
                Address = "json:device.sensors[0].value",
                ValueType = DataTypeEnum.Double 
            });
            Assert.AreEqual(VaribaleStatusTypeEnum.Good, result.StatusType, $"Unexpected status: {result.Message}");
            Assert.AreEqual(100.5, Convert.ToDouble(result.Value));
        }

        [TestMethod]
        public void TestJsonParsingErrors()
        {
            var parser = new TCPParser(_deviceName, _logger);

            // 测试无效JSON
            parser.SetTestData(Encoding.UTF8.GetBytes("invalid json"));
            var result = parser.TestRead(new DriverAddressIoArgModel 
            { 
                Address = "json:value",
                ValueType = DataTypeEnum.Double 
            });
            Assert.AreEqual(VaribaleStatusTypeEnum.Bad, result.StatusType);

            // 测试无效路径
            parser.SetTestData(Encoding.UTF8.GetBytes(@"{""value"": 100}"));
            var pathResult = parser.TestRead(new DriverAddressIoArgModel 
            { 
                Address = "json:nonexistent",
                ValueType = DataTypeEnum.Double 
            });
            Assert.AreEqual(VaribaleStatusTypeEnum.Bad, pathResult.StatusType);
        }

        [TestMethod]
        public void TestByteDataParsing()
        {
            var parser = new TCPParser(_deviceName, _logger);
            
            // 测试数据：0x01 0x02 0x03 0x04 0xFF 0xFE
            byte[] testData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0xFF, 0xFE };
            parser.SetTestData(testData);

            // 测试 Int16 读取 (0x0102 = 258)
            var int16Result = parser.TestRead(new DriverAddressIoArgModel 
            { 
                Address = "0,2",
                ValueType = DataTypeEnum.Int16 
            });
            Assert.AreEqual(VaribaleStatusTypeEnum.Good, int16Result.StatusType, $"Unexpected status: {int16Result.Message}");
            Assert.AreEqual(258, Convert.ToInt16(int16Result.Value));

            // 测试 Int32 读取 (0x01020304 = 16909060)
            var int32Result = parser.TestRead(new DriverAddressIoArgModel 
            { 
                Address = "0,4",
                ValueType = DataTypeEnum.Int32 
            });
            Assert.AreEqual(VaribaleStatusTypeEnum.Good, int32Result.StatusType, $"Unexpected status: {int32Result.Message}");
            Assert.AreEqual(16909060, Convert.ToInt32(int32Result.Value));

            // 测试 Int16 读取 (0xFFFE = -2)
            var negativeResult = parser.TestRead(new DriverAddressIoArgModel 
            { 
                Address = "4,2",
                ValueType = DataTypeEnum.Int16 
            });
            Assert.AreEqual(VaribaleStatusTypeEnum.Good, negativeResult.StatusType, $"Unexpected status: {negativeResult.Message}");
            Assert.AreEqual(-2, Convert.ToInt16(negativeResult.Value));

            // 测试单字节读取
            var byteResult = parser.TestRead(new DriverAddressIoArgModel 
            { 
                Address = "0,1",
                ValueType = DataTypeEnum.Byte 
            });
            Assert.AreEqual(VaribaleStatusTypeEnum.Good, byteResult.StatusType, $"Unexpected status: {byteResult.Message}");
            Assert.AreEqual(0x01, Convert.ToByte(byteResult.Value));
        }

        [TestMethod]
        public void TestByteDataErrors()
        {
            var parser = new TCPParser(_deviceName, _logger);
            byte[] testData = new byte[] { 0x01, 0x02, 0x03 };
            parser.SetTestData(testData);

            // 测试越界访问
            var result = parser.TestRead(new DriverAddressIoArgModel 
            { 
                Address = "2,2",  // 尝试从位置2读取2字节，但只有1字节可用
                ValueType = DataTypeEnum.Int16 
            });
            Assert.AreEqual(VaribaleStatusTypeEnum.Bad, result.StatusType);

            // 测试无效的地址格式
            var formatResult = parser.TestRead(new DriverAddressIoArgModel 
            { 
                Address = "invalid",
                ValueType = DataTypeEnum.Int16 
            });
            Assert.AreEqual(VaribaleStatusTypeEnum.Bad, formatResult.StatusType);

            // 测试负数起始位置
            var negativeResult = parser.TestRead(new DriverAddressIoArgModel 
            { 
                Address = "-1,1",
                ValueType = DataTypeEnum.Byte 
            });
            Assert.AreEqual(VaribaleStatusTypeEnum.Bad, negativeResult.StatusType);
        }
    }
}
