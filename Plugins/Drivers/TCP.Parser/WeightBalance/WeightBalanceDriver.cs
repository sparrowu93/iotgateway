using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PluginInterface;
using TCP.Parser.Models;

namespace TCP.Parser.WeightBalance
{
    /// <summary>
    /// 称重定重心台驱动
    /// 支持两个服务：
    /// 1. 设备服务器（192.168.1.200:204）
    /// 2. 测试设备服务器（192.168.1.180:402）
    /// </summary>
    [DriverSupported("WeightBalance")]
    [DriverInfo("WeightBalance", "V1.0.0", "Copyright IoTGateway 2024-12-26")]
    public class WeightBalanceDriver : TCPParser
    {
        private const string PRODUCT_TYPE_MPTA5000 = "MPTA-5000";
        private const string SERVICE_TEST_COMMAND = "START_TEST";

        public WeightBalanceDriver(string device, ILogger logger) : base(device, logger)
        {
        }

        /// <summary>
        /// 测试服务状态
        /// </summary>
        public async Task<WeightBalanceTestResult> TestServiceStatusAsync()
        {
            var command = new CommandDefinition
            {
                CommandId = "TEST_SERVICE",
                CommandData = Encoding.UTF8.GetBytes($"cmd:{SERVICE_TEST_COMMAND}:MPTA-5000:1:无测试任务"),
                ResponsePattern = "regex:MPTA-5000.*",
                Timeout = CommandTimeout,
                RetryCount = CommandRetries,
                RetryInterval = CommandRetryInterval
            };

            var response = await ExecuteCommandAsync(command);
            if (!response.Success)
            {
                throw new Exception($"服务测试失败: {response.Error}");
            }

            return new WeightBalanceTestResult { IsServiceAvailable = true };
        }

        /// <summary>
        /// 解析测试结果数据
        /// </summary>
        protected override object ParseCommandResponse(byte[] responseData, string responsePattern)
        {
            // 首先调用基类的解析方法检查响应模式匹配
            var baseResult = base.ParseCommandResponse(responseData, responsePattern);
            if (baseResult == null)
            {
                return null;
            }

            try
            {
                string jsonString = Encoding.UTF8.GetString(responseData);
                using var jsonDoc = JsonDocument.Parse(jsonString);
                var root = jsonDoc.RootElement;

                // 验证产品类型
                if (root.TryGetProperty("PRODUCT_TYPE", out var productType) &&
                    productType.GetString() == PRODUCT_TYPE_MPTA5000)
                {
                    return new WeightBalanceData
                    {
                        ProductType = productType.GetString(),
                        ProductNumber = GetStringProperty(root, "PRODUCT_NUMBER"),
                        ProductState = GetStringProperty(root, "PRODUCT_STATE"),
                        TestMassX = GetDoubleProperty(root, "TEST_MASS_X"),
                        TestMassY = GetDoubleProperty(root, "TEST_MASS_Y"),
                        TestMassZ = GetDoubleProperty(root, "TEST_MASS_Z"),
                        TestSita = GetDoubleProperty(root, "TEST_SITA"),
                        ConversionMass = GetDoubleProperty(root, "CONVERSION_MASS"),
                        ConversionLengX = GetDoubleProperty(root, "CONVERSION_LENG_X"),
                        ConversionLengY = GetDoubleProperty(root, "CONVERSION_LENG_Y"),
                        ConversionLengZ = GetDoubleProperty(root, "CONVERSION_LENG_Z"),
                        ConversionRo = GetDoubleProperty(root, "CONVERSION_RO"),
                        BalanceLengX = GetDoubleProperty(root, "BALANC_LENG_X"),
                        BalanceLengY = GetDoubleProperty(root, "BALANC_LENG_Y"),
                        BalanceLengZ = GetDoubleProperty(root, "BALANC_LENG_Z"),
                        TestResult = GetStringProperty(root, "TEST_RESULT")
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "解析测试结果数据失败");
            }

            return null;
        }

        private string GetStringProperty(JsonElement root, string propertyName)
        {
            return root.TryGetProperty(propertyName, out var element) ? element.GetString() : string.Empty;
        }

        private double GetDoubleProperty(JsonElement root, string propertyName)
        {
            return root.TryGetProperty(propertyName, out var element) ? 
                   element.TryGetDouble(out var value) ? value : 0.0 : 0.0;
        }

        public override DriverReturnValueModel TestRead(DriverAddressIoArgModel ioarg)
        {
            var ret = new DriverReturnValueModel { StatusType = VaribaleStatusTypeEnum.Good };

            try
            {
                // 处理特定的测试命令
                if (ioarg.Address.StartsWith("cmd:"))
                {
                    var parts = ioarg.Address.Split(':');
                    if (parts.Length >= 4 && parts[1] == SERVICE_TEST_COMMAND)
                    {
                        // 构建响应格式：MPTA-5000:2:"产品可号":"产品编号":"型号状态"
                        ret.Value = $"{PRODUCT_TYPE_MPTA5000}:2:\"{parts[2]}\":\"{parts[3]}\":\"测试中\"";
                        return ret;
                    }
                }

                // 处理JSON数据解析
                if (ioarg.Address.StartsWith("json:"))
                {
                    string jsonPath = ioarg.Address.Substring(5);
                    if (string.IsNullOrEmpty(_latestData?.ToString()))
                    {
                        ret.StatusType = VaribaleStatusTypeEnum.Bad;
                        ret.Message = "无数据";
                        return ret;
                    }

                    try
                    {
                        using var jsonDoc = JsonDocument.Parse(_latestData);
                        var element = jsonDoc.RootElement;
                        foreach (string path in jsonPath.Split('.'))
                        {
                            element = element.GetProperty(path);
                        }
                        ret.Value = element.GetRawText();
                        return ret;
                    }
                    catch (Exception ex)
                    {
                        ret.StatusType = VaribaleStatusTypeEnum.Bad;
                        ret.Message = $"JSON解析错误: {ex.Message}";
                        return ret;
                    }
                }

                return base.TestRead(ioarg);
            }
            catch (Exception ex)
            {
                ret.StatusType = VaribaleStatusTypeEnum.Bad;
                ret.Message = $"测试读取失败: {ex.Message}";
                return ret;
            }
        }
    }
}
