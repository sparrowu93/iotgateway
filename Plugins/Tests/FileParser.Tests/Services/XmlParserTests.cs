using System.IO;
using FileParser.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FileParser.Tests.Services;

public class XmlParserTests
{
    private readonly XmlParser _parser;
    private readonly string _testDataPath;

    public XmlParserTests()
    {
        _parser = new XmlParser();
        _testDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData");
    }

    [Fact]
    public async Task ParseAsync_ValidXml_ReturnsCorrectJson()
    {
        // Arrange
        var filePath = Path.Combine(_testDataPath, "test.xml");

        // Act
        var result = await _parser.ParseAsync(filePath);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("1", result["device"]["@id"].ToString());
        Assert.Equal("测试设备", result["device"]["@name"].ToString());
        Assert.Equal("25.6", result["device"]["status"]["temperature"].ToString());
        Assert.Equal("60", result["device"]["status"]["humidity"].ToString());
        
        var measurements = result["device"]["data"]["measurement"] as JArray;
        Assert.NotNull(measurements);
        Assert.Equal(2, measurements.Count);
        Assert.Equal("2024-01-01T12:00:00", measurements[0]["@time"].ToString());
        Assert.Equal("1013.25", measurements[0]["value"][0]["#text"].ToString());
    }

    [Fact]
    public void CanParse_XmlExtension_ReturnsTrue()
    {
        Assert.True(_parser.CanParse(".xml"));
        Assert.True(_parser.CanParse(".XML"));
    }

    [Fact]
    public void CanParse_NonXmlExtension_ReturnsFalse()
    {
        Assert.False(_parser.CanParse(".csv"));
        Assert.False(_parser.CanParse(".txt"));
    }

    [Fact]
    public async Task ParseAsync_InvalidXml_ThrowsException()
    {
        // Arrange
        var filePath = Path.Combine(_testDataPath, "invalid.xml");

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(() => _parser.ParseAsync(filePath));
    }

    [Fact]
    public async Task ParseAsync_FileNotFound_ThrowsException()
    {
        // Arrange
        var filePath = Path.Combine(_testDataPath, "nonexistent.xml");

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => _parser.ParseAsync(filePath));
    }
}
