using System.Threading.Tasks;
using FileParser.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FileParser.Tests.Services;

public class DataCacheTests
{
    private readonly DataCache _cache;

    public DataCacheTests()
    {
        _cache = new DataCache();
    }

    [Fact]
    public void UpdateData_NewData_AddsToCache()
    {
        // Arrange
        var fileName = "test.xml";
        var data = new JObject { ["test"] = "value" };

        // Act
        _cache.UpdateData(fileName, data);

        // Assert
        var result = _cache.GetData(fileName);
        Assert.NotNull(result);
        Assert.Equal("value", result["test"].ToString());
    }

    [Fact]
    public void UpdateData_ExistingData_UpdatesCache()
    {
        // Arrange
        var fileName = "test.xml";
        var data1 = new JObject { ["test"] = "value1" };
        var data2 = new JObject { ["test"] = "value2" };

        // Act
        _cache.UpdateData(fileName, data1);
        _cache.UpdateData(fileName, data2);

        // Assert
        var result = _cache.GetData(fileName);
        Assert.NotNull(result);
        Assert.Equal("value2", result["test"].ToString());
    }

    [Fact]
    public void GetData_NonexistentFile_ReturnsNull()
    {
        // Act
        var result = _cache.GetData("nonexistent.xml");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetAllFiles_ReturnsCorrectCount()
    {
        // Arrange
        _cache.UpdateData("test1.xml", new JObject());
        _cache.UpdateData("test2.xml", new JObject());

        // Act
        var files = _cache.GetAllFiles();

        // Assert
        Assert.Equal(2, files.Count());
    }

    [Fact]
    public void GetAllData_ReturnsCorrectCount()
    {
        // Arrange
        _cache.UpdateData("test1.xml", new JObject());
        _cache.UpdateData("test2.xml", new JObject());

        // Act
        var data = _cache.GetAllData();

        // Assert
        Assert.Equal(2, data.Count());
    }

    [Fact]
    public void RemoveData_RemovesFromCache()
    {
        // Arrange
        var fileName = "test.xml";
        _cache.UpdateData(fileName, new JObject());

        // Act
        _cache.RemoveData(fileName);

        // Assert
        Assert.Null(_cache.GetData(fileName));
    }

    [Fact]
    public void Clear_EmptiesCache()
    {
        // Arrange
        _cache.UpdateData("test1.xml", new JObject());
        _cache.UpdateData("test2.xml", new JObject());

        // Act
        _cache.Clear();

        // Assert
        Assert.Empty(_cache.GetAllFiles());
        Assert.Empty(_cache.GetAllData());
    }

    [Fact]
    public async Task ConcurrentAccess_ThreadSafe()
    {
        // Arrange
        var tasks = new List<Task>();
        for (int i = 0; i < 100; i++)
        {
            var fileName = $"test{i}.xml";
            var data = new JObject { ["value"] = i };
            tasks.Add(Task.Run(() => _cache.UpdateData(fileName, data)));
        }

        // Act
        await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(100, _cache.GetAllFiles().Count());
        Assert.Equal(100, _cache.GetAllData().Count());
    }
}
