using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using OPC.UaClient;
using Microsoft.Extensions.Logging;
using DriverOpcUaClient;
using PluginInterface;

namespace OPC.UaClient.Tests
{
    // Custom test logger that implements ILogger directly
    public class TestLogger : ILogger
    {
        private readonly ITestOutputHelper _output;

        public TestLogger(ITestOutputHelper output)
        {
            _output = output;
        }

        public IDisposable BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            _output.WriteLine($"[{logLevel}] {formatter(state, exception)}");
        }
    }

    public class DeviceUaClientTests
    {
        private readonly ITestOutputHelper _output;
        private readonly ILogger _logger;

        public DeviceUaClientTests(ITestOutputHelper output)
        {
            _output = output;
            _logger = new TestLogger(output);
        }

        [Fact]
        public void TestConnection()
        {
            Console.WriteLine("Starting OPC UA connection test...");
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                Console.WriteLine("Creating device instance...");
                var device = new OpcUaClient("TestDevice", _logger);
                Console.WriteLine($"Device instance created in {stopwatch.ElapsedMilliseconds}ms");

                // Configure device
                Console.WriteLine("Configuring device parameters...");
                device.ServerUrl = "opc.tcp://Karels-MacBook-Air.local:53530/OPCUA/SimulationServer";
                device.Timeout = 15000;
                Console.WriteLine($"Device configuration completed in {stopwatch.ElapsedMilliseconds}ms");

                // Connect to server
                Console.WriteLine("Attempting to connect to OPC UA server...");
                Console.WriteLine($"Server URL: {device.ServerUrl}");
                Console.WriteLine($"Connect timeout: {device.Timeout}ms");
                
                var connectResult = device.Connect();
                Console.WriteLine($"Connection attempt completed in {stopwatch.ElapsedMilliseconds}ms with result: {connectResult}");

                Assert.True(connectResult, "Connection to OPC UA server failed");
                
                // If connected, try to disconnect
                if (connectResult)
                {
                    Console.WriteLine("Connection successful, attempting to disconnect...");
                    device.Close();
                    Console.WriteLine("Disconnected successfully");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception occurred: {ex.GetType().Name}");
                Console.WriteLine($"Message: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.GetType().Name}");
                    Console.WriteLine($"Inner message: {ex.InnerException.Message}");
                }
                
                throw;
            }
            finally
            {
                stopwatch.Stop();
                Console.WriteLine($"Test completed in {stopwatch.ElapsedMilliseconds}ms");
            }
        }

        [Fact]
        public void TestBrowse()
        {
            Console.WriteLine("Starting OPC UA browse test...");
            var stopwatch = Stopwatch.StartNew();
            OpcUaClient device = null;
            
            try
            {
                // 创建并连接设备
                device = new OpcUaClient("TestDevice", _logger);
                device.ServerUrl = "opc.tcp://Karels-MacBook-Air.local:53530/OPCUA/SimulationServer";
                device.Timeout = 15000;
                
                var connectResult = device.Connect();
                Assert.True(connectResult, "Connection to OPC UA server failed");
                Console.WriteLine($"Connected to server in {stopwatch.ElapsedMilliseconds}ms");
                
                // 浏览根节点
                Console.WriteLine("Browsing root folder...");
                var rootBrowseResult = device.Browse(new DriverAddressIoArgModel { Address = "" });
                Assert.Equal(VaribaleStatusTypeEnum.Good, rootBrowseResult.StatusType);
                Assert.NotNull(rootBrowseResult.Value);
                
                // 输出根节点浏览结果
                var rootNodes = rootBrowseResult.Value as List<Dictionary<string, object>>;
                Assert.NotNull(rootNodes);
                
                Console.WriteLine($"Found {rootNodes.Count} nodes at root level:");
                foreach (var node in rootNodes)
                {
                    Console.WriteLine($"Node: {node["DisplayName"]} ({node["NodeClass"]}) - {node["NodeId"]}");
                }
                
                // 查找对象节点并继续浏览
                var objectsNode = rootNodes.FirstOrDefault(n => n["DisplayName"].ToString() == "Objects");
                Assert.True(objectsNode != null, "Objects node not found");
                
                // 浏览对象节点
                Console.WriteLine($"Browsing Objects node ({objectsNode["NodeId"]})...");
                var objectsBrowseResult = device.Browse(new DriverAddressIoArgModel { Address = objectsNode["NodeId"].ToString() });
                Assert.Equal(VaribaleStatusTypeEnum.Good, objectsBrowseResult.StatusType);
                
                var objectsChildren = objectsBrowseResult.Value as List<Dictionary<string, object>>;
                Assert.NotNull(objectsChildren);
                
                Console.WriteLine($"Found {objectsChildren.Count} nodes under Objects:");
                foreach (var node in objectsChildren)
                {
                    Console.WriteLine($"Node: {node["DisplayName"]} ({node["NodeClass"]}) - {node["NodeId"]}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception occurred: {ex.GetType().Name}");
                Console.WriteLine($"Message: {ex.Message}");
                throw;
            }
            finally
            {
                device?.Close();
                stopwatch.Stop();
                Console.WriteLine($"Test completed in {stopwatch.ElapsedMilliseconds}ms");
            }
        }

        [Fact]
        public void TestRead()
        {
            Console.WriteLine("Starting OPC UA read test...");
            var stopwatch = Stopwatch.StartNew();
            OpcUaClient device = null;
            
            try
            {
                // 创建并连接设备
                device = new OpcUaClient("TestDevice", _logger);
                device.ServerUrl = "opc.tcp://Karels-MacBook-Air.local:53530/OPCUA/SimulationServer";
                device.Timeout = 15000;
                
                var connectResult = device.Connect();
                Assert.True(connectResult, "Connection to OPC UA server failed");
                Console.WriteLine($"Connected to server in {stopwatch.ElapsedMilliseconds}ms");
                
                // 浏览根节点查找可读取的变量
                Console.WriteLine("Browsing to find variables for reading...");
                var rootBrowseResult = device.Browse(new DriverAddressIoArgModel { Address = "" });
                var rootNodes = rootBrowseResult.Value as List<Dictionary<string, object>>;
                
                // 查找对象节点
                var objectsNode = rootNodes.FirstOrDefault(n => n["DisplayName"].ToString() == "Objects");
                var objectsBrowseResult = device.Browse(new DriverAddressIoArgModel { Address = objectsNode["NodeId"].ToString() });
                var objectsChildren = objectsBrowseResult.Value as List<Dictionary<string, object>>;
                
                // 搜索服务器下的节点
                var serverNode = objectsChildren.FirstOrDefault(n => n["DisplayName"].ToString().Contains("Server"));
                if (serverNode != null)
                {
                    var serverBrowseResult = device.Browse(new DriverAddressIoArgModel { Address = serverNode["NodeId"].ToString() });
                    var serverChildren = serverBrowseResult.Value as List<Dictionary<string, object>>;
                    
                    // 查找具体变量
                    foreach (var node in serverChildren)
                    {
                        if (node["NodeClass"].ToString() == "Variable")
                        {
                            Console.WriteLine($"Reading variable: {node["DisplayName"]} ({node["NodeId"]})");
                            var readResult = device.Read(new DriverAddressIoArgModel 
                            { 
                                Address = node["NodeId"].ToString(),
                                ValueType = DataTypeEnum.Utf8String
                            });
                            
                            Console.WriteLine($"Result: {readResult.StatusType}, Value: {readResult.Value}, Message: {readResult.Message}");
                            Assert.Equal(VaribaleStatusTypeEnum.Good, readResult.StatusType);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception occurred: {ex.GetType().Name}");
                Console.WriteLine($"Message: {ex.Message}");
                throw;
            }
            finally
            {
                device?.Close();
                stopwatch.Stop();
                Console.WriteLine($"Test completed in {stopwatch.ElapsedMilliseconds}ms");
            }
        }

        [Fact]
        public async Task TestWriteAsync()
        {
            Console.WriteLine("Starting OPC UA write test...");
            var stopwatch = Stopwatch.StartNew();
            OpcUaClient device = null;
            
            try
            {
                // 创建并连接设备
                device = new OpcUaClient("TestDevice", _logger);
                device.ServerUrl = "opc.tcp://Karels-MacBook-Air.local:53530/OPCUA/SimulationServer";
                device.Timeout = 15000;
                
                var connectResult = device.Connect();
                Assert.True(connectResult, "Connection to OPC UA server failed");
                Console.WriteLine($"Connected to server in {stopwatch.ElapsedMilliseconds}ms");
                
                // 浏览查找可写入的变量
                Console.WriteLine("Browsing to find variables for writing...");
                var rootBrowseResult = device.Browse(new DriverAddressIoArgModel { Address = "" });
                var rootNodes = rootBrowseResult.Value as List<Dictionary<string, object>>;
                
                // 查找对象节点
                var objectsNode = rootNodes.FirstOrDefault(n => n["DisplayName"].ToString() == "Objects");
                var objectsBrowseResult = device.Browse(new DriverAddressIoArgModel { Address = objectsNode["NodeId"].ToString() });
                var objectsChildren = objectsBrowseResult.Value as List<Dictionary<string, object>>;
                
                // 搜索服务器下的节点
                var myDeviceNode = objectsChildren.FirstOrDefault(n => n["DisplayName"].ToString().Contains("MyDevice"));
                if (myDeviceNode != null)
                {
                    var myDeviceBrowseResult = device.Browse(new DriverAddressIoArgModel { Address = myDeviceNode["NodeId"].ToString() });
                    var myDeviceChildren = myDeviceBrowseResult.Value as List<Dictionary<string, object>>;
                    
                    // 查找具体变量
                    var targetVariable = myDeviceChildren.FirstOrDefault(n => 
                        n["NodeClass"].ToString() == "Variable" && 
                        (n["DisplayName"].ToString().Contains("Temperature") || n["DisplayName"].ToString().Contains("Pressure")));
                    
                    if (targetVariable != null)
                    {
                        Console.WriteLine($"Writing to variable: {targetVariable["DisplayName"]} ({targetVariable["NodeId"]})");
                        
                        // 读取原始值
                        var readResult = device.Read(new DriverAddressIoArgModel 
                        { 
                            Address = targetVariable["NodeId"].ToString(),
                            ValueType = DataTypeEnum.Double
                        });
                        Console.WriteLine($"Original value: {readResult.Value}");
                        
                        // 写入新值
                        double newValue = 42.0;
                        var writeResult = await device.WriteAsync("TestWriteRequest", "Write", new DriverAddressIoArgModel
                        {
                            Address = targetVariable["NodeId"].ToString(),
                            Value = newValue
                        });
                        
                        Console.WriteLine($"Write result: {writeResult.IsSuccess}, Description: {writeResult.Description}");
                        Assert.True(writeResult.IsSuccess, "Write operation failed");
                        
                        // 验证写入结果
                        var verifyResult = device.Read(new DriverAddressIoArgModel 
                        { 
                            Address = targetVariable["NodeId"].ToString(),
                            ValueType = DataTypeEnum.Double
                        });
                        Console.WriteLine($"New value after write: {verifyResult.Value}");
                        Assert.Equal(newValue, verifyResult.Value);
                    }
                    else
                    {
                        Console.WriteLine("No suitable writable variable found for testing");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception occurred: {ex.GetType().Name}");
                Console.WriteLine($"Message: {ex.Message}");
                throw;
            }
            finally
            {
                device?.Close();
                stopwatch.Stop();
                Console.WriteLine($"Test completed in {stopwatch.ElapsedMilliseconds}ms");
            }
        }

        [Fact]
        public void TestReadMultiple()
        {
            Console.WriteLine("Starting OPC UA multiple read test...");
            var stopwatch = Stopwatch.StartNew();
            OpcUaClient device = null;
            
            try
            {
                // 创建并连接设备
                device = new OpcUaClient("TestDevice", _logger);
                device.ServerUrl = "opc.tcp://Karels-MacBook-Air.local:53530/OPCUA/SimulationServer";
                device.Timeout = 15000;
                
                var connectResult = device.Connect();
                Assert.True(connectResult, "Connection to OPC UA server failed");
                Console.WriteLine($"Connected to server in {stopwatch.ElapsedMilliseconds}ms");
                
                // 先浏览查找可读取的变量
                List<string> nodesToRead = new List<string>();
                
                // 浏览根节点查找可读取的变量
                Console.WriteLine("Browsing to find variables for reading...");
                var rootBrowseResult = device.Browse(new DriverAddressIoArgModel { Address = "" });
                var rootNodes = rootBrowseResult.Value as List<Dictionary<string, object>>;
                
                // 查找对象节点
                var objectsNode = rootNodes.FirstOrDefault(n => n["DisplayName"].ToString() == "Objects");
                var objectsBrowseResult = device.Browse(new DriverAddressIoArgModel { Address = objectsNode["NodeId"].ToString() });
                var objectsChildren = objectsBrowseResult.Value as List<Dictionary<string, object>>;
                
                // 查找服务器和设备节点
                foreach (var node in objectsChildren)
                {
                    var childBrowseResult = device.Browse(new DriverAddressIoArgModel { Address = node["NodeId"].ToString() });
                    if (childBrowseResult.StatusType == VaribaleStatusTypeEnum.Good)
                    {
                        var children = childBrowseResult.Value as List<Dictionary<string, object>>;
                        
                        // 收集变量节点
                        foreach (var childNode in children)
                        {
                            if (childNode["NodeClass"].ToString() == "Variable")
                            {
                                nodesToRead.Add(childNode["NodeId"].ToString());
                                Console.WriteLine($"Added for reading: {childNode["DisplayName"]} ({childNode["NodeId"]})");
                                
                                // 最多收集5个节点
                                if (nodesToRead.Count >= 5)
                                    break;
                            }
                        }
                    }
                    
                    if (nodesToRead.Count >= 5)
                        break;
                }
                
                // 确保有节点可读
                Assert.True(nodesToRead.Count > 0, "No variable nodes found for reading");
                
                // 执行批量读取
                Console.WriteLine($"Reading {nodesToRead.Count} nodes at once...");
                var readMultipleResult = device.ReadMultiple(new DriverAddressIoArgModel 
                { 
                    Address = string.Join(",", nodesToRead),
                    ValueType = DataTypeEnum.Utf8String
                });
                
                Console.WriteLine($"ReadMultiple result: {readMultipleResult.StatusType}, Message: {readMultipleResult.Message}");
                Assert.Equal(VaribaleStatusTypeEnum.Good, readMultipleResult.StatusType);
                
                // 输出读取结果
                var resultDict = readMultipleResult.Value as Dictionary<string, object>;
                Assert.NotNull(resultDict);
                
                foreach (var kvp in resultDict)
                {
                    Console.WriteLine($"Node: {kvp.Key}, Value: {kvp.Value}");
                }
                
                Assert.Equal(nodesToRead.Count, resultDict.Count);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception occurred: {ex.GetType().Name}");
                Console.WriteLine($"Message: {ex.Message}");
                throw;
            }
            finally
            {
                device?.Close();
                stopwatch.Stop();
                Console.WriteLine($"Test completed in {stopwatch.ElapsedMilliseconds}ms");
            }
        }
    }
}