using System;

namespace TCP.Parser.Models
{
    /// <summary>
    /// 命令定义模型
    /// </summary>
    public class CommandDefinition
    {
        /// <summary>
        /// 命令ID，用于标识特定命令
        /// </summary>
        public string CommandId { get; set; }

        /// <summary>
        /// 命令数据
        /// </summary>
        public byte[] CommandData { get; set; }

        /// <summary>
        /// 响应匹配模式
        /// 可以是：
        /// 1. 字节模式（hex格式，如：01-02-03）
        /// 2. 正则表达式（以regex:开头，如：regex:^OK.*）
        /// 3. JSON路径（以json:开头，如：json:$.result）
        /// </summary>
        public string ResponsePattern { get; set; }

        /// <summary>
        /// 命令超时时间（毫秒）
        /// </summary>
        public int Timeout { get; set; } = 5000;

        /// <summary>
        /// 重试次数
        /// </summary>
        public int RetryCount { get; set; } = 3;

        /// <summary>
        /// 重试间隔（毫秒）
        /// </summary>
        public int RetryInterval { get; set; } = 1000;
    }
}
