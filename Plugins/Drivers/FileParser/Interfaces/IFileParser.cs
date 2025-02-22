using Newtonsoft.Json.Linq;

namespace FileParser.Interfaces;

public interface IFileParser
{
    /// <summary>
    /// 解析文件内容为JSON对象
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>解析后的JSON对象</returns>
    Task<JObject> ParseAsync(string filePath);

    /// <summary>
    /// 检查是否可以解析指定扩展名的文件
    /// </summary>
    /// <param name="fileExtension">文件扩展名</param>
    /// <returns>是否可以解析</returns>
    bool CanParse(string fileExtension);
}
