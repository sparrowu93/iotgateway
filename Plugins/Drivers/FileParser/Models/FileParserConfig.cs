using IoTGateway.Model;
using PluginInterface;

namespace FileParser.Models;

public class FileParserConfig
{
    [ConfigParameter("服务器类型")]
    public string ServerType { get; set; } = "FTP";

    [ConfigParameter("服务器地址")]
    public string ServerAddress { get; set; } = "localhost";

    [ConfigParameter("用户名")]
    public string Username { get; set; } = "anonymous";

    [ConfigParameter("密码")]
    public string Password { get; set; } = "";

    [ConfigParameter("监控路径")]
    public string MonitorPath { get; set; } = "/";

    [ConfigParameter("文件前缀")]
    public string FilePrefix { get; set; } = "";

    [ConfigParameter("扫描间隔")]
    public int ScanInterval { get; set; } = 5000;

    [ConfigParameter("文件模式")]
    public string FilePattern { get; set; } = "*.xml";
}
