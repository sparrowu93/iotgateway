using System;

namespace TCP.Parser.Models
{
    /// <summary>
    /// 命令响应模型
    /// </summary>
    public class CommandResponse
    {
        /// <summary>
        /// 命令是否执行成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 解析后的响应数据
        /// </summary>
        public object ParsedData { get; set; }

        /// <summary>
        /// 原始响应数据
        /// </summary>
        public byte[] RawData { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        public string Error { get; set; }

        /// <summary>
        /// 命令执行时间（毫秒）
        /// </summary>
        public long ExecutionTime { get; set; }
    }
}
