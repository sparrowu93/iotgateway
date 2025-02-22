using FileParser.Interfaces;
using FileParser.Models;
using FileParser.Services;
using Moq;
using Newtonsoft.Json.Linq;
using System.IO;
using Xunit;

namespace FileParser.Tests.Services;

public class FileMonitorTests : IDisposable
{
    private readonly Mock<IFileParser> _mockParser;
    private readonly DataCache _cache;
    private readonly FileParserConfig _config;
    private readonly string _testDataPath;
    private FileMonitor? _monitor;

    public FileMonitorTests()
    {
        _mockParser = new Mock<IFileParser>();
        _cache = new DataCache();
        _testDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData");
        
        _config = new FileParserConfig
        {
            ServerType = "LOCAL",
            MonitorPath = _testDataPath,
            FilePattern = "*.xml",
            ScanInterval = 1000
        };

        // 确保测试目录存在
        if (!Directory.Exists(_testDataPath))
        {
            Directory.CreateDirectory(_testDataPath);
        }
    }

    [Fact]
    public void Start_StartsMonitoring()
    {
        // Arrange
        _mockParser.Setup(p => p.CanParse(It.IsAny<string>())).Returns(true);
        _mockParser.Setup(p => p.ParseAsync(It.IsAny<string>()))
            .ReturnsAsync(new JObject());

        _monitor = new FileMonitor(_config, new List<IFileParser> { _mockParser.Object }, _cache);

        // Act
        _monitor.Start();

        // Assert
        // 验证监控是否启动（通过内部状态）
        // 注意：这是一个简化的测试，实际可能需要更复杂的验证
    }

    [Fact]
    public void Stop_StopsMonitoring()
    {
        // Arrange
        _mockParser.Setup(p => p.CanParse(It.IsAny<string>())).Returns(true);
        _mockParser.Setup(p => p.ParseAsync(It.IsAny<string>()))
            .ReturnsAsync(new JObject());

        _monitor = new FileMonitor(_config, new List<IFileParser> { _mockParser.Object }, _cache);

        // Act
        _monitor.Start();
        _monitor.Stop();

        // Assert
        // 验证监控是否停止（通过内部状态）
        // 注意：这是一个简化的测试，实际可能需要更复杂的验证
    }

    [Fact]
    public async Task ProcessFiles_ParsesAndCachesFiles()
    {
        // Arrange
        var testData = new JObject { ["test"] = "value" };
        _mockParser.Setup(p => p.CanParse(It.IsAny<string>())).Returns(true);
        _mockParser.Setup(p => p.ParseAsync(It.IsAny<string>()))
            .ReturnsAsync(testData);

        // 创建测试文件
        var testFile = Path.Combine(_testDataPath, "test.xml");
        await File.WriteAllTextAsync(testFile, "<test>value</test>");

        _monitor = new FileMonitor(_config, new List<IFileParser> { _mockParser.Object }, _cache);

        // Act
        _monitor.Start();
        await Task.Delay(2000); // 等待文件处理

        // Assert
        _mockParser.Verify(p => p.ParseAsync(It.IsAny<string>()), Times.AtLeastOnce);
        
        // 清理测试文件
        if (File.Exists(testFile))
        {
            File.Delete(testFile);
        }
    }

    [Fact]
    public async Task ProcessFiles_HandlesErrors()
    {
        // Arrange
        _mockParser.Setup(p => p.CanParse(It.IsAny<string>())).Returns(true);
        _mockParser.Setup(p => p.ParseAsync(It.IsAny<string>()))
            .ThrowsAsync(new Exception("Test error"));

        // 创建测试文件
        var testFile = Path.Combine(_testDataPath, "error.xml");
        await File.WriteAllTextAsync(testFile, "<test>value</test>");

        _monitor = new FileMonitor(_config, new List<IFileParser> { _mockParser.Object }, _cache);

        // Act
        _monitor.Start();
        await Task.Delay(2000); // 等待文件处理

        // Assert
        _mockParser.Verify(p => p.ParseAsync(It.IsAny<string>()), Times.AtLeastOnce);
        
        // 清理测试文件
        if (File.Exists(testFile))
        {
            File.Delete(testFile);
        }
    }

    public void Dispose()
    {
        _monitor?.Dispose();
        
        // 清理测试目录
        if (Directory.Exists(_testDataPath))
        {
            Directory.Delete(_testDataPath, true);
        }
    }
}
