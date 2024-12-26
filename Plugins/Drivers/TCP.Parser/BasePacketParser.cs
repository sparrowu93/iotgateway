using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace TCP.Parser
{
    public class PacketField
    {
        public string Name { get; set; }
        public int Start { get; set; }
        public int Length { get; set; }
        public string Type { get; set; }
    }

    public class PacketFormat
    {
        public List<PacketField> Fields { get; set; }
    }

    public class BasePacketParser
    {
        private readonly PacketFormat _format;

        public BasePacketParser(string formatJson)
        {
            _format = JsonSerializer.Deserialize<PacketFormat>(formatJson);
        }

        public Dictionary<string, object> Parse(byte[] data)
        {
            var result = new Dictionary<string, object>();

            foreach (var field in _format.Fields)
            {
                // 计算实际长度
                int length = field.Length;
                if (length == -1)
                {
                    length = data.Length - field.Start;
                }

                // 确保不超出数据范围
                if (field.Start + length > data.Length)
                {
                    throw new ArgumentException($"Field {field.Name} exceeds data length");
                }

                // 根据类型解析数据
                object value = ParseValue(data, field.Start, length, field.Type);
                result[field.Name] = value;
            }

            return result;
        }

        private object ParseValue(byte[] data, int start, int length, string type)
        {
            byte[] fieldData = new byte[length];
            Array.Copy(data, start, fieldData, 0, length);

            switch (type.ToLower())
            {
                case "hex":
                    return BitConverter.ToString(fieldData);

                case "int":
                    switch (length)
                    {
                        case 1:
                            return fieldData[0];
                        case 2:
                            return BitConverter.ToInt16(fieldData, 0);
                        case 4:
                            return BitConverter.ToInt32(fieldData, 0);
                        default:
                            throw new ArgumentException($"Invalid length for int type: {length}");
                    }

                case "uint":
                    switch (length)
                    {
                        case 1:
                            return fieldData[0];
                        case 2:
                            return BitConverter.ToUInt16(fieldData, 0);
                        case 4:
                            return BitConverter.ToUInt32(fieldData, 0);
                        default:
                            throw new ArgumentException($"Invalid length for uint type: {length}");
                    }

                case "float":
                    if (length == 4)
                        return BitConverter.ToSingle(fieldData, 0);
                    throw new ArgumentException("Float type requires 4 bytes");

                case "double":
                    if (length == 8)
                        return BitConverter.ToDouble(fieldData, 0);
                    throw new ArgumentException("Double type requires 8 bytes");

                case "string":
                    return Encoding.ASCII.GetString(fieldData);

                case "utf8":
                    return Encoding.UTF8.GetString(fieldData);

                default:
                    throw new ArgumentException($"Unsupported type: {type}");
            }
        }
    }
}
