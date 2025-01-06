using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using TCP.DKScrew.Models;

namespace TCP.DKScrew
{
    public class DKScrewDriverTest
    {
        static async Task Main(string[] args)
        {
            // 创建日志工厂
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
            });
            var logger = loggerFactory.CreateLogger<DKScrewPlugin>();

            // 创建驱动实例
            var driver = new DKScrewPlugin(logger);

            // 配置驱动
            var config = new DKScrewDeviceConfig
            {
                IpAddress = "192.168.1.100", // 替换为实际的IP地址
                Port = 4001,
                EnableCurveData = true,
                CurveDataInterval = 100,
                StatusUpdateInterval = 1000
            };
            driver.Configure(config.ToString());

            try
            {
                Console.WriteLine("正在连接设备...");
                if (await driver.ConnectAsync())
                {
                    Console.WriteLine("设备连接成功！");

                    // 注册变量变化处理
                    while (true)
                    {
                        // 读取并显示状态
                        var isReady = driver.ReadNode(DeviceVariables.IsReady);
                        var isRunning = driver.ReadNode(DeviceVariables.IsRunning);
                        var isOK = driver.ReadNode(DeviceVariables.IsOK);
                        var isNG = driver.ReadNode(DeviceVariables.IsNG);
                        var torque = driver.ReadNode(DeviceVariables.FinalTorque);
                        var angle = driver.ReadNode(DeviceVariables.FinalAngle);

                        Console.WriteLine($"\r就绪: {isReady}, 运行: {isRunning}, OK: {isOK}, NG: {isNG}, 扭矩: {torque}, 角度: {angle}");

                        // 等待用户输入命令
                        if (Console.KeyAvailable)
                        {
                            var key = Console.ReadKey(true);
                            switch (key.Key)
                            {
                                case ConsoleKey.S:
                                    await driver.WriteNode(DeviceVariables.StartMotor, true);
                                    Console.WriteLine("\n启动电机");
                                    break;
                                case ConsoleKey.T:
                                    await driver.WriteNode(DeviceVariables.StopMotor, true);
                                    Console.WriteLine("\n停止电机");
                                    break;
                                case ConsoleKey.L:
                                    await driver.WriteNode(DeviceVariables.LoosenMotor, true);
                                    Console.WriteLine("\n松开电机");
                                    break;
                                case ConsoleKey.D1:
                                case ConsoleKey.D2:
                                case ConsoleKey.D3:
                                case ConsoleKey.D4:
                                case ConsoleKey.D5:
                                case ConsoleKey.D6:
                                case ConsoleKey.D7:
                                case ConsoleKey.D8:
                                    int pset = key.Key - ConsoleKey.D0;
                                    await driver.WriteNode(DeviceVariables.SelectPset, pset);
                                    Console.WriteLine($"\n选择Pset {pset}");
                                    break;
                                case ConsoleKey.Q:
                                    Console.WriteLine("\n退出程序");
                                    return;
                            }
                        }

                        await Task.Delay(100);
                    }
                }
                else
                {
                    Console.WriteLine("设备连接失败！");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生错误: {ex.Message}");
            }
            finally
            {
                await driver.DisconnectAsync();
                driver.Dispose();
            }
        }
    }
}
