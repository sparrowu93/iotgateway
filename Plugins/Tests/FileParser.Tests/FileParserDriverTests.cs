using FileParser.Models;
using IoTGateway.Model;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using PluginInterface;

namespace FileParser.Tests;

public class FileParserDriverTests : IDisposable
{
    private readonly FileParserDriver _driver;
    private readonly string _testDataPath;
    private readonly Mock<ILogger> _mockLogger;

    public FileParserDriverTests()
    {
        _mockLogger = new Mock<ILogger>();
        _driver = new FileParserDriver(_mockLogger.Object, "TEST001");
        _testDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData");
    }

    [Fact]
    public void Connect_WithValidConfig_ReturnsTrue()
    {
        // Arrange
        var config = new FileParserConfig
        {
            ServerType = "FTP",
            ServerAddress = "localhost",
            Username = "test",
            Password = "test",
            MonitorPath = _testDataPath,
            FilePattern = "*.xml"
        };

        // Act
        _driver.SetConfig(config);
        var result = _driver.Connect();

        // Assert
        Assert.True(result);
        Assert.True(_driver.IsConnected);
    }

    [Fact]
    public void Connect_WithInvalidConfig_ReturnsFalse()
    {
        // Arrange
        var config = new FileParserConfig
        {
            ServerType = "Invalid",
            ServerAddress = "localhost",
            Username = "test",
            Password = "test",
            MonitorPath = _testDataPath,
            FilePattern = "*.xml"
        };

        // Act
        _driver.SetConfig(config);
        var result = _driver.Connect();

        // Assert
        Assert.False(result);
        Assert.False(_driver.IsConnected);
    }

    [Fact]
    public void Close_ReturnsTrue()
    {
        // Arrange
        _driver.SetConfig(new FileParserConfig());
        _driver.Connect();

        // Act
        var result = _driver.Close();

        // Assert
        Assert.True(result);
        Assert.False(_driver.IsConnected);
    }

    [Fact]
    public void ReadNode_StatusVariable_ReturnsValue()
    {
        // Arrange
        _driver.SetConfig(new FileParserConfig());
        _driver.Connect();

        // Act
        var result = _driver.ReadNode("status.connected");

        // Assert
        Assert.True((bool)result!);
    }

    [Fact]
    public void ReadNode_InvalidAddress_ReturnsNull()
    {
        // Act
        var result = _driver.ReadNode("invalid.address");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Read_ValidAddress_ReturnsValue()
    {
        // Arrange
        _driver.SetConfig(new FileParserConfig());
        _driver.Connect();

        // Act
        var result = _driver.Read(new DriverAddressIoArgModel { Address = "status.connected" });

        // Assert
        Assert.NotNull(result);
        Assert.True((bool)result.Value!);
        Assert.Equal("读取成功", result.Message);
    }

    [Fact]
    public async Task WriteAsync_ReturnsNotSupported()
    {
        // Act
        var result = await _driver.WriteAsync("TEST001", "any.address", new DriverAddressIoArgModel());

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("FileParser驱动不支持写入操作", result.Description);
    }

    public void Dispose()
    {
        _driver.Dispose();
    }
}
