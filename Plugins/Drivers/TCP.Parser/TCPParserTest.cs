using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PluginInterface;

namespace TCP.Parser
{
    public class TCPParserTest
    {
        private readonly ILogger _logger;
        private readonly string _deviceName;

        public TCPParserTest()
        {
            _logger = new ConsoleLogger();
            _deviceName = "TCPParserTest";
        }

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
            if (result.StatusType != VaribaleStatusTypeEnum.Good)
                throw new Exception($"Expected status to be Good, got {result.StatusType}. Message: {result.Message}");
            if (Convert.ToDouble(result.Value) != 100.5)
                throw new Exception($"Expected value to be 100.5, got {result.Value}");

            // 测试布尔值读取
            var statusResult = parser.TestRead(new DriverAddressIoArgModel 
            { 
                Address = "json:status",
                ValueType = DataTypeEnum.Bool 
            });
            if (statusResult.StatusType != VaribaleStatusTypeEnum.Good)
                throw new Exception($"Expected status to be Good, got {statusResult.StatusType}. Message: {statusResult.Message}");
            if (!Convert.ToBoolean(statusResult.Value))
                throw new Exception("Expected status to be true");
        }

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
            if (result.StatusType != VaribaleStatusTypeEnum.Good)
                throw new Exception($"Expected status to be Good, got {result.StatusType}. Message: {result.Message}");
            if (Convert.ToDouble(result.Value) != 100.5)
                throw new Exception($"Expected value to be 100.5, got {result.Value}");
        }

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
            if (result.StatusType != VaribaleStatusTypeEnum.Bad)
                throw new Exception($"Expected status to be Bad for invalid JSON, got {result.StatusType}");

            // 测试无效路径
            parser.SetTestData(Encoding.UTF8.GetBytes(@"{""value"": 100}"));
            var pathResult = parser.TestRead(new DriverAddressIoArgModel 
            { 
                Address = "json:nonexistent",
                ValueType = DataTypeEnum.Double 
            });
            if (pathResult.StatusType != VaribaleStatusTypeEnum.Bad)
                throw new Exception($"Expected status to be Bad for invalid path, got {pathResult.StatusType}");
        }

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
            if (int16Result.StatusType != VaribaleStatusTypeEnum.Good)
                throw new Exception($"Expected status to be Good, got {int16Result.StatusType}. Message: {int16Result.Message}");
            if (Convert.ToInt16(int16Result.Value) != 258)
                throw new Exception($"Expected Int16 value to be 258, got {int16Result.Value}");

            // 测试 Int32 读取 (0x01020304 = 16909060)
            var int32Result = parser.TestRead(new DriverAddressIoArgModel 
            { 
                Address = "0,4",
                ValueType = DataTypeEnum.Int32 
            });
            if (int32Result.StatusType != VaribaleStatusTypeEnum.Good)
                throw new Exception($"Expected status to be Good, got {int32Result.StatusType}. Message: {int32Result.Message}");
            if (Convert.ToInt32(int32Result.Value) != 16909060)
                throw new Exception($"Expected Int32 value to be 16909060, got {int32Result.Value}");

            // 测试 Int16 读取 (0xFFFE = -2)
            var negativeResult = parser.TestRead(new DriverAddressIoArgModel 
            { 
                Address = "4,2",
                ValueType = DataTypeEnum.Int16 
            });
            if (negativeResult.StatusType != VaribaleStatusTypeEnum.Good)
                throw new Exception($"Expected status to be Good, got {negativeResult.StatusType}. Message: {negativeResult.Message}");
            if (Convert.ToInt16(negativeResult.Value) != -2)
                throw new Exception($"Expected Int16 value to be -2, got {negativeResult.Value}");

            // 测试单字节读取
            var byteResult = parser.TestRead(new DriverAddressIoArgModel 
            { 
                Address = "0,1",
                ValueType = DataTypeEnum.Byte 
            });
            if (byteResult.StatusType != VaribaleStatusTypeEnum.Good)
                throw new Exception($"Expected status to be Good, got {byteResult.StatusType}. Message: {byteResult.Message}");
            if (Convert.ToByte(byteResult.Value) != 0x01)
                throw new Exception($"Expected Byte value to be 1, got {byteResult.Value}");
        }

        public void TestByteDataErrors()
        {
            var parser = new TCPParser(_deviceName, _logger);
            byte[] testData = new byte[] { 0x01, 0x02, 0x03 };
            parser.SetTestData(testData);

            // 测试越界访问
            var result = parser.TestRead(new DriverAddressIoArgModel 
            { 
                Address = "2,2",  // 尝试从位置2读取2字节，但只有1字节可用
                ValueType = DataTypeEnum.Uint16 
            });
            if (result.StatusType != VaribaleStatusTypeEnum.Bad)
                throw new Exception($"Expected status to be Bad for out-of-range access, got {result.StatusType}");

            // 测试无效的地址格式
            var formatResult = parser.TestRead(new DriverAddressIoArgModel 
            { 
                Address = "invalid",
                ValueType = DataTypeEnum.Uint16 
            });
            if (formatResult.StatusType != VaribaleStatusTypeEnum.Bad)
                throw new Exception($"Expected status to be Bad for invalid address format, got {formatResult.StatusType}");

            // 测试负数起始位置
            var negativeResult = parser.TestRead(new DriverAddressIoArgModel 
            { 
                Address = "-1,1",
                ValueType = DataTypeEnum.Byte 
            });
            if (negativeResult.StatusType != VaribaleStatusTypeEnum.Bad)
                throw new Exception($"Expected status to be Bad for negative start position, got {negativeResult.StatusType}");
        }

        public void RunAllTests()
        {
            Console.WriteLine("Running JSON parsing tests...");
            
            Console.WriteLine("Testing basic JSON parsing...");
            TestJsonParsing();
            
            Console.WriteLine("Testing nested JSON parsing...");
            TestNestedJsonParsing();
            
            Console.WriteLine("Testing JSON parsing errors...");
            TestJsonParsingErrors();
            
            Console.WriteLine("Testing byte data parsing...");
            TestByteDataParsing();
            
            Console.WriteLine("Testing byte data errors...");
            TestByteDataErrors();
            
            Console.WriteLine("All tests passed!");
        }
    }
}
