using System.Globalization;
using CsvHelper;
using FileParser.Interfaces;
using Newtonsoft.Json.Linq;
using System.IO;

namespace FileParser.Services;

public class CsvParser : IFileParser
{
    public bool CanParse(string fileExtension)
    {
        return fileExtension.Equals(".csv", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<JObject> ParseAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"找不到文件: {filePath}", filePath);
        }

        try
        {
            using var reader = new StreamReader(filePath);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

            // 读取表头
            csv.Read();
            csv.ReadHeader();
            var headers = csv.HeaderRecord;

            // 读取所有记录
            var records = new List<Dictionary<string, string>>();
            while (csv.Read())
            {
                var record = new Dictionary<string, string>();
                foreach (var header in headers)
                {
                    record[header] = csv.GetField(header) ?? string.Empty;
                }
                records.Add(record);
            }

            // 转换为JSON格式
            var json = new JObject
            {
                ["headers"] = new JArray(headers),
                ["records"] = JArray.FromObject(records)
            };

            return json;
        }
        catch (Exception ex)
        {
            throw new Exception($"解析CSV文件失败: {ex.Message}", ex);
        }
    }
}
