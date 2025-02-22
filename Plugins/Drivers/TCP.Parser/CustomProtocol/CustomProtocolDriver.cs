using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PluginInterface;
using TCP.Parser.Models;

namespace TCP.Parser.CustomProtocol
{
    /// <summary>
    /// 自定义协议驱动示例
    /// 协议格式：
    /// 1. 命令格式：[STX][CMD][LEN][DATA][CRC][ETX]
    ///    - STX: 起始符 0x02
    ///    - CMD: 命令字 1字节
    ///    - LEN: 数据长度 1字节
    ///    - DATA: 数据内容 N字节
    ///    - CRC: 校验和 1字节 (CMD+LEN+DATA的和取低字节)
    ///    - ETX: 结束符 0x03
    /// 2. 响应格式：[STX][CMD][STATUS][LEN][DATA][CRC][ETX]
    ///    - STX: 起始符 0x02
    ///    - CMD: 命令字 1字节
    ///    - STATUS: 状态 1字节 (0x00:成功, 其他:失败)
    ///    - LEN: 数据长度 1字节
    ///    - DATA: 数据内容 N字节
    ///    - CRC: 校验和 1字节 (CMD+STATUS+LEN+DATA的和取低字节)
    ///    - ETX: 结束符 0x03
    /// </summary>
    [DriverSupported("CustomProtocol")]
    [DriverInfo("CustomProtocol", "V1.0.0", "Copyright IoTGateway 2024-12-26")]
    public class CustomProtocolDriver : TCPParser
    {
        private const byte STX = 0x02;
        private const byte ETX = 0x03;
        private const byte CMD_READ = 0x01;
        private const byte CMD_WRITE = 0x02;
        private const byte STATUS_SUCCESS = 0x00;

        public CustomProtocolDriver(string device, ILogger logger) : base(device, logger)
        {
        }

        /// <summary>
        /// 发送读取命令
        /// </summary>
        /// <param name="address">地址</param>
        /// <returns>读取结果</returns>
        public async Task<string> ReadValueAsync(string address)
        {
            byte[] data = Encoding.ASCII.GetBytes(address);
            var command = new CommandDefinition
            {
                CommandId = $"READ_{address}",
                CommandData = BuildCommand(CMD_READ, data),
                ResponsePattern = BuildResponsePattern(CMD_READ),
                Timeout = CommandTimeout,
                RetryCount = CommandRetries,
                RetryInterval = CommandRetryInterval
            };

            var response = await ExecuteCommandAsync(command);
            if (!response.Success)
            {
                throw new Exception($"读取失败: {response.Error}");
            }

            return ParseReadResponse(response.RawData);
        }

        /// <summary>
        /// 发送写入命令
        /// </summary>
        /// <param name="address">地址</param>
        /// <param name="value">值</param>
        public async Task WriteValueAsync(string address, string value)
        {
            byte[] addressData = Encoding.ASCII.GetBytes(address);
            byte[] valueData = Encoding.ASCII.GetBytes(value);
            byte[] data = new byte[addressData.Length + 1 + valueData.Length];
            Array.Copy(addressData, 0, data, 0, addressData.Length);
            data[addressData.Length] = 0x2C; // 分隔符 ','
            Array.Copy(valueData, 0, data, addressData.Length + 1, valueData.Length);

            var command = new CommandDefinition
            {
                CommandId = $"WRITE_{address}",
                CommandData = BuildCommand(CMD_WRITE, data),
                ResponsePattern = BuildResponsePattern(CMD_WRITE),
                Timeout = CommandTimeout,
                RetryCount = CommandRetries,
                RetryInterval = CommandRetryInterval
            };

            var response = await ExecuteCommandAsync(command);
            if (!response.Success)
            {
                throw new Exception($"写入失败: {response.Error}");
            }

            if (!IsSuccessResponse(response.RawData))
            {
                throw new Exception("写入失败: 设备返回错误状态");
            }
        }

        /// <summary>
        /// 构建命令数据包
        /// </summary>
        private byte[] BuildCommand(byte cmd, byte[] data)
        {
            byte[] packet = new byte[data.Length + 5];
            packet[0] = STX;
            packet[1] = cmd;
            packet[2] = (byte)data.Length;
            Array.Copy(data, 0, packet, 3, data.Length);
            
            // 计算校验和
            byte crc = cmd;
            crc += (byte)data.Length;
            foreach (byte b in data)
            {
                crc += b;
            }
            
            packet[packet.Length - 2] = crc;
            packet[packet.Length - 1] = ETX;
            
            return packet;
        }

        /// <summary>
        /// 构建响应匹配模式
        /// </summary>
        private string BuildResponsePattern(byte cmd)
        {
            // 匹配响应的起始字节和命令字
            return $"{STX:X2}-{cmd:X2}";
        }

        /// <summary>
        /// 解析读取响应
        /// </summary>
        private string ParseReadResponse(byte[] response)
        {
            if (response == null || response.Length < 7)
            {
                throw new Exception("响应数据格式错误");
            }

            if (response[0] != STX || response[response.Length - 1] != ETX)
            {
                throw new Exception("响应数据帧错误");
            }

            byte status = response[2];
            if (status != STATUS_SUCCESS)
            {
                throw new Exception($"读取失败: 状态码 0x{status:X2}");
            }

            byte len = response[3];
            byte[] data = new byte[len];
            Array.Copy(response, 4, data, 0, len);

            // 验证CRC
            byte crc = response[1]; // CMD
            crc += status;
            crc += len;
            foreach (byte b in data)
            {
                crc += b;
            }

            if (crc != response[response.Length - 2])
            {
                throw new Exception("响应数据校验错误");
            }

            return Encoding.ASCII.GetString(data);
        }

        /// <summary>
        /// 检查响应是否成功
        /// </summary>
        private bool IsSuccessResponse(byte[] response)
        {
            if (response == null || response.Length < 7)
            {
                return false;
            }

            return response[0] == STX && 
                   response[response.Length - 1] == ETX && 
                   response[2] == STATUS_SUCCESS;
        }

        /// <summary>
        /// 重写响应解析方法
        /// </summary>
        protected override object ParseCommandResponse(byte[] responseData, string responsePattern)
        {
            // 首先调用基类的解析方法检查响应模式匹配
            var baseResult = base.ParseCommandResponse(responseData, responsePattern);
            if (baseResult == null)
            {
                return null;
            }

            // 验证响应格式
            if (responseData.Length < 7 || 
                responseData[0] != STX || 
                responseData[responseData.Length - 1] != ETX)
            {
                return null;
            }

            // 验证CRC
            byte crc = 0;
            for (int i = 1; i < responseData.Length - 2; i++)
            {
                crc += responseData[i];
            }

            if (crc != responseData[responseData.Length - 2])
            {
                return null;
            }

            return responseData;
        }
    }
}
