using System;
using Microsoft.Extensions.Logging;

namespace TCP.Parser
{
    class Program
    {
        static void Main(string[] args)
        {
            // 创建日志记录器
            ILogger logger = new ConsoleLogger();

            // 创建并运行测试
            var test = new TCPParserTest();
            test.RunAllTests();
        }
    }
}
