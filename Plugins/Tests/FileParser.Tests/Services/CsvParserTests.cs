using System.IO;
using FileParser.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FileParser.Tests.Services;

public class CsvParserTests
{
    private readonly CsvParser _parser;
    private readonly string _testDataPath;

    public CsvParserTests()
    {
        _parser = new CsvParser();
        _testDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData");
    }

    [Fact]
    public async Task ParseAsync_ValidCsv_ReturnsCorrectJson()
    {
        // Arrange
        var filePath = Path.Combine(_testDataPath, "test.csv");

        // Act
        var result = await _parser.ParseAsync(filePath);

        // Assert
        Assert.NotNull(result);
        
        var headers = result["headers"] as JArray;
        Assert.NotNull(headers);
        Assert.Equal(5, headers.Count);
        Assert.Equal("Time", headers[0].ToString());
        Assert.Equal("DeviceId", headers[1].ToString());

        var records = result["records"] as JArray;
        Assert.NotNull(records);
        Assert.Equal(3, records.Count);
        
        var firstRecord = records[0] as JObject;
        Assert.NotNull(firstRecord);
        Assert.Equal("2024-01-01 12:00:00", firstRecord["Time"].ToString());
        Assert.Equal("25.6", firstRecord["Temperature"].ToString());
        Assert.Equal("1013.25", firstRecord["Pressure"].ToString());
    }

    [Fact]
    public void CanParse_CsvExtension_ReturnsTrue()
    {
        Assert.True(_parser.CanParse(".csv"));
        Assert.True(_parser.CanParse(".CSV"));
    }

    [Fact]
    public void CanParse_NonCsvExtension_ReturnsFalse()
    {
        Assert.False(_parser.CanParse(".xml"));
        Assert.False(_parser.CanParse(".txt"));
    }

    [Fact]
    public async Task ParseAsync_InvalidCsv_ThrowsException()
    {
        // Arrange
        var filePath = Path.Combine(_testDataPath, "invalid.csv");

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(() => _parser.ParseAsync(filePath));
    }

    [Fact]
    public async Task ParseAsync_FileNotFound_ThrowsException()
    {
        // Arrange
        var filePath = Path.Combine(_testDataPath, "nonexistent.csv");

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => _parser.ParseAsync(filePath));
    }
}
