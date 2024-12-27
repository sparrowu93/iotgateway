using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using PluginInterface;
using TCP.Parser;
using DriverTest;

class Program
{
    static void Main(string[] args)
    {
        // Setup logger
        ILogger logger = new ConsoleLogger();

        // Create an instance of TCPParser
        TCPParser parser = new TCPParser("TestDevice", logger)
        {
            IpAddress = "127.0.0.1",
            Port = 15021,
            Timeout = 3000,
            MinPeriod = 100  // Reduced for faster testing
        };

        Console.WriteLine("TCP Parser Test Application");
        Console.WriteLine("==========================");

        // Test cases for different data types and formats
        TestTcpParser(parser);

        Console.WriteLine("\nTests completed. Press Enter to exit...");
        try
        {
            Console.ReadLine();
        }
        catch
        {
            // If running without console, just exit
        }
    }

    static void TestTcpParser(TCPParser parser)
    {
        if (!parser.Connect())
        {
            Console.WriteLine("Failed to connect to the server.");
            return;
        }

        Console.WriteLine("Connected successfully.");
        
        try
        {
            // Wait for data to be received
            Thread.Sleep(500);

            // Test case 1: Read 2 bytes as Int16
            TestRead(parser, "0,2", DataTypeEnum.Int16, "Reading 2 bytes as Int16");

            // Test case 2: Read 4 bytes as Float with 2 decimal places
            TestRead(parser, "2,4,2", DataTypeEnum.Float, "Reading 4 bytes as Float with 2 decimal places");

            // Test case 3: Read string data with different encodings
            TestRead(parser, "6,10,ascii", DataTypeEnum.AsciiString, "Reading ASCII string");
            TestRead(parser, "6,10,utf8", DataTypeEnum.AsciiString, "Reading UTF8 string");
            TestRead(parser, "6,10,hex", DataTypeEnum.AsciiString, "Reading string as HEX");

            // Test case 4: Read from position to end
            TestRead(parser, "16,-1", DataTypeEnum.AsciiString, "Reading from position 16 to end");

            // Test case 5: Test boundary cases
            TestRead(parser, "0,1", DataTypeEnum.Bool, "Reading single byte as Bool");
            TestRead(parser, "0,8", DataTypeEnum.Int64, "Reading 8 bytes as Int64");

            // Test case 6: Test error cases
            TestRead(parser, "999,1", DataTypeEnum.Byte, "Testing out of bounds read");
            TestRead(parser, "0", DataTypeEnum.Byte, "Testing invalid address format");
        }
        finally
        {
            parser.Close();
            Console.WriteLine("Connection closed.");
        }
    }

    static void TestRead(TCPParser parser, string address, DataTypeEnum dataType, string description)
    {
        Console.WriteLine($"\nTest: {description}");
        Console.WriteLine($"Address: {address}, Type: {dataType}");

        var ioArg = new DriverAddressIoArgModel 
        { 
            Address = address,
            ValueType = dataType
        };

        var result = parser.Read(ioArg);
        Console.WriteLine($"Status: {result.StatusType}");
        Console.WriteLine($"Value: {result.Value}");
        if (!string.IsNullOrEmpty(result.Message))
        {
            Console.WriteLine($"Message: {result.Message}");
        }
    }

    static void PerformRead(TCPParser client)
    {
        Console.Write("Enter the address to read (e.g., SP_1): ");
        string address = Console.ReadLine();

        var ioArg = new DriverAddressIoArgModel { Address = address };
        var result = client.Read(ioArg);

        if (result.StatusType == VaribaleStatusTypeEnum.Good)
        {
            Console.WriteLine($"Read successful. Value: {result.Value}");
        }
        else
        {
            Console.WriteLine($"Read failed. Message: {result.Message}");
        }
    }
}

// Simple console logger implementation
class ConsoleLogger : ILogger
{
    public IDisposable BeginScope<TState>(TState state) => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
    {
        Console.WriteLine($"[{logLevel}] {formatter(state, exception)}");
    }
}