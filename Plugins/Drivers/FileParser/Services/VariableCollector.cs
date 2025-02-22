using Newtonsoft.Json.Linq;

namespace FileParser.Services;

public class VariableCollector
{
    private readonly DataCache _cache;

    public VariableCollector(DataCache cache)
    {
        _cache = cache;
    }

    public object? GetValue(string address)
    {
        try
        {
            // 处理状态变量
            if (address.StartsWith("status.", StringComparison.OrdinalIgnoreCase))
            {
                return GetStatusValue(address);
            }

            // 处理JSON数据变量
            if (address.StartsWith("json.", StringComparison.OrdinalIgnoreCase))
            {
                return GetJsonValue(address);
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private object? GetStatusValue(string address)
    {
        var field = address.Substring("status.".Length).ToLower();
        return field switch
        {
            "connected" => true, // 暂时固定返回true
            "lastfiletime" => _cache.GetAllFiles()
                .Select(f => _cache.GetLastModified(f))
                .DefaultIfEmpty(DateTime.MinValue)
                .Max(),
            "lastfilename" => _cache.GetAllFiles()
                .OrderByDescending(f => _cache.GetLastModified(f))
                .FirstOrDefault() ?? string.Empty,
            "filecount" => _cache.GetAllFiles().Count(),
            _ => null
        };
    }

    private object? GetJsonValue(string address)
    {
        // 移除json.前缀
        var path = address.Substring("json.".Length);

        // 获取最新的数据
        var latestFile = _cache.GetAllFiles()
            .OrderByDescending(f => _cache.GetLastModified(f))
            .FirstOrDefault();

        if (latestFile == null) return null;

        var data = _cache.GetData(latestFile);
        if (data == null) return null;

        // 处理数组索引
        if (path.Contains("[") && path.Contains("]"))
        {
            return GetArrayValue(data, path);
        }

        // 处理通配符
        if (path.Contains("*"))
        {
            return GetWildcardValue(data, path);
        }

        // 处理普通路径
        return GetPathValue(data, path);
    }

    private object? GetArrayValue(JObject data, string path)
    {
        try
        {
            var parts = path.Split(new[] { '[', ']' }, StringSplitOptions.RemoveEmptyEntries);
            var arrayPath = parts[0];
            var index = int.Parse(parts[1]);
            var remainingPath = parts.Length > 2 ? string.Join(".", parts.Skip(2)) : null;

            var array = data.SelectToken(arrayPath) as JArray;
            if (array == null || index >= array.Count) return null;

            var item = array[index];
            return remainingPath != null ? item.SelectToken(remainingPath) : item;
        }
        catch
        {
            return null;
        }
    }

    private object? GetWildcardValue(JObject data, string path)
    {
        try
        {
            // 将通配符 * 替换为 [*]，这是JPath中的正确通配符语法
            var jpath = path.Replace(".*.", ".[*].");
            jpath = jpath.EndsWith(".*") ? jpath.Substring(0, jpath.Length - 2) + "[*]" : jpath;
            jpath = jpath.StartsWith("*.") ? "[*]" + jpath.Substring(1) : jpath;
            
            var tokens = data.SelectTokens(jpath);
            return tokens.FirstOrDefault()?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private object? GetPathValue(JObject data, string path)
    {
        try
        {
            return data.SelectToken(path)?.ToString();
        }
        catch
        {
            return null;
        }
    }
}
