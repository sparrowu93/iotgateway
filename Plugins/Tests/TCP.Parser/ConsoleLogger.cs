using Microsoft.Extensions.Logging;

namespace TCP.Parser.Tests
{
    internal class ConsoleLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            switch (logLevel)
            {
                case LogLevel.Debug:
                    Console.WriteLine($"[Debug] {message}");
                    break;
                case LogLevel.Information:
                    Console.WriteLine($"[Info] {message}");
                    break;
                case LogLevel.Warning:
                    Console.WriteLine($"[Warn] {message}");
                    break;
                case LogLevel.Error:
                    Console.WriteLine($"[Error] {message}");
                    break;
                default:
                    Console.WriteLine($"[{logLevel}] {message}");
                    break;
            }
        }
    }
}
