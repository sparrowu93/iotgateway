using System.Collections.Concurrent;
using Newtonsoft.Json.Linq;

namespace FileParser.Services;

public class DataCache
{
    private readonly ConcurrentDictionary<string, JObject> _cache = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastModified = new();

    public void UpdateData(string fileName, JObject data)
    {
        _cache.AddOrUpdate(fileName, data, (_, _) => data);
        _lastModified.AddOrUpdate(fileName, DateTime.Now, (_, _) => DateTime.Now);
    }

    public JObject? GetData(string fileName)
    {
        _cache.TryGetValue(fileName, out var data);
        return data;
    }

    public DateTime GetLastModified(string fileName)
    {
        _lastModified.TryGetValue(fileName, out var time);
        return time;
    }

    public IEnumerable<string> GetAllFiles()
    {
        return _cache.Keys;
    }

    public IEnumerable<JObject> GetAllData()
    {
        return _cache.Values;
    }

    public void RemoveData(string fileName)
    {
        _cache.TryRemove(fileName, out _);
        _lastModified.TryRemove(fileName, out _);
    }

    public void Clear()
    {
        _cache.Clear();
        _lastModified.Clear();
    }
}
