using FileParser.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FileParser.Tests.Services;

public class VariableCollectorTests
{
    private readonly DataCache _cache;
    private readonly VariableCollector _collector;

    public VariableCollectorTests()
    {
        _cache = new DataCache();
        _collector = new VariableCollector(_cache);

        // 添加测试数据
        var testData = new JObject
        {
            ["device"] = new JObject
            {
                ["status"] = new JObject
                {
                    ["temperature"] = 25.6,
                    ["humidity"] = 60
                },
                ["data"] = new JObject
                {
                    ["measurement"] = new JArray
                    {
                        new JObject
                        {
                            ["time"] = "2024-01-01T12:00:00",
                            ["values"] = new JObject
                            {
                                ["pressure"] = 1013.25,
                                ["wind"] = 5.2
                            }
                        },
                        new JObject
                        {
                            ["time"] = "2024-01-01T12:01:00",
                            ["values"] = new JObject
                            {
                                ["pressure"] = 1013.20,
                                ["wind"] = 5.5
                            }
                        }
                    }
                }
            }
        };

        _cache.UpdateData("test.xml", testData);
    }

    [Fact]
    public void GetValue_StatusConnected_ReturnsTrue()
    {
        var result = _collector.GetValue("status.connected");
        Assert.True((bool)result!);
    }

    [Fact]
    public void GetValue_StatusLastFileName_ReturnsFileName()
    {
        var result = _collector.GetValue("status.lastfilename");
        Assert.Equal("test.xml", result);
    }

    [Fact]
    public void GetValue_StatusFileCount_ReturnsOne()
    {
        var result = _collector.GetValue("status.filecount");
        Assert.Equal(1, result);
    }

    [Fact]
    public void GetValue_SimpleJsonPath_ReturnsValue()
    {
        var result = _collector.GetValue("json.device.status.temperature");
        Assert.Equal("25.6", result?.ToString());
    }

    [Fact]
    public void GetValue_ArrayAccess_ReturnsValue()
    {
        var result = _collector.GetValue("json.device.data.measurement[0].values.pressure");
        Assert.Equal("1013.25", result?.ToString());
    }

    [Fact]
    public void GetValue_Wildcard_ReturnsFirstMatch()
    {
        var result = _collector.GetValue("json.device.data.measurement.*.values.pressure");
        Assert.Equal("1013.25", result?.ToString());
    }

    [Fact]
    public void GetValue_InvalidPath_ReturnsNull()
    {
        var result = _collector.GetValue("json.nonexistent.path");
        Assert.Null(result);
    }

    [Fact]
    public void GetValue_InvalidAddress_ReturnsNull()
    {
        var result = _collector.GetValue("invalid.address");
        Assert.Null(result);
    }

    [Fact]
    public void GetValue_EmptyCache_ReturnsDefaultValues()
    {
        // Arrange
        var emptyCache = new DataCache();
        var collector = new VariableCollector(emptyCache);

        // Act & Assert
        Assert.True((bool)collector.GetValue("status.connected")!);
        Assert.Equal(string.Empty, collector.GetValue("status.lastfilename"));
        Assert.Equal(0, collector.GetValue("status.filecount"));
        Assert.Null(collector.GetValue("json.any.path"));
    }
}
