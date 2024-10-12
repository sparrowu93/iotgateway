using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using DriverIEC60870;
using PluginInterface;

class Program
{
    static void Main(string[] args)
    {
        // Setup logger (you might want to use a proper logging framework in a real application)
        ILogger logger = new ConsoleLogger();

        // Create an instance of IEC60870Client
        IEC60870Client client = new IEC60870Client("TestDevice", logger)
        {
            IpAddress = "192.168.10.5", // Replace with your server's IP
            Port = 2404,
            AsduAddress = 1,
            Timeout = 5000,
            MinPeriod = 1000
        };

        Console.WriteLine("IEC60870 Client Console Application");
        Console.WriteLine("===================================");

        // Connect to the server
        if (client.Connect())
        {
            Console.WriteLine("Connected successfully.");

            // Main loop
            bool running = true;
            while (running)
            {
                DriverAddressIoArgModel argModel = new DriverAddressIoArgModel
                {
                    Address = "MMEF_11"
                };
                DriverReturnValueModel drm = client.Read(argModel);
                Console.WriteLine($"{drm.Message} {drm.Value}");
                
                DriverAddressIoArgModel argModel1 = new DriverAddressIoArgModel
                {
                    Address = "SP_10"
                };
                DriverReturnValueModel drm1 = client.Read(argModel1);
                Console.WriteLine($"{drm1.Message} {drm1.Value}");
                Thread.Sleep(1000);
            }

            // Disconnect
            client.Close();
            Console.WriteLine("Disconnected from the server.");
        }
        else
        {
            Console.WriteLine("Failed to connect to the server.");
        }

        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }

    static void PerformRead(IEC60870Client client)
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