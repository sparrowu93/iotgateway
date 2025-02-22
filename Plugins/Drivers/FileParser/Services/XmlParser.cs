using System.Xml.Linq;
using FileParser.Interfaces;
using Newtonsoft.Json.Linq;

namespace FileParser.Services;

public class XmlParser : IFileParser
{
    public bool CanParse(string fileExtension)
    {
        return fileExtension.Equals(".xml", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<JObject> ParseAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"找不到文件: {filePath}", filePath);
        }

        try
        {
            // 读取XML文件
            var xml = await File.ReadAllTextAsync(filePath);
            var doc = XDocument.Parse(xml);

            // 转换为JObject
            var json = new JObject();
            ConvertXmlToJson(doc.Root, json);

            return json;
        }
        catch (Exception ex)
        {
            throw new Exception($"解析XML文件失败: {ex.Message}", ex);
        }
    }

    private void ConvertXmlToJson(XElement? element, JObject parent)
    {
        if (element == null) return;

        // 处理属性
        foreach (var attribute in element.Attributes())
        {
            parent[$"@{attribute.Name.LocalName}"] = attribute.Value;
        }

        // 处理子元素
        var childGroups = element.Elements()
            .GroupBy(e => e.Name.LocalName);

        foreach (var group in childGroups)
        {
            var key = group.Key;
            if (group.Count() > 1)
            {
                // 多个相同名称的元素，创建数组
                var array = new JArray();
                foreach (var child in group)
                {
                    if (!child.HasElements && !child.HasAttributes)
                    {
                        array.Add(child.Value);
                    }
                    else
                    {
                        var obj = new JObject();
                        ConvertXmlToJson(child, obj);
                        array.Add(obj);
                    }
                }
                parent[key] = array;
            }
            else
            {
                // 单个元素
                var child = group.First();
                if (!child.HasElements && !child.HasAttributes)
                {
                    parent[key] = child.Value;
                }
                else
                {
                    var obj = new JObject();
                    ConvertXmlToJson(child, obj);
                    parent[key] = obj;
                }
            }
        }

        // 处理文本内容
        if (!element.HasElements && element.HasAttributes)
        {
            parent["#text"] = element.Value;
        }
    }
}
