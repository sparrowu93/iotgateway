using System.Collections.Generic;
using PluginInterface;

namespace TCP.Parser.Models
{
    public static class TCPParserAddressDefinitions
    {
        public static Dictionary<string, AddressDefinitionInfo> GetDefinitions()
        {
            return new Dictionary<string, AddressDefinitionInfo>
            {
                {
                    "ByteArray",
                    new AddressDefinitionInfo
                    {
                        Description = "字节数组",
                        DataType = DataTypeEnum.AsciiString,
                        AddressFormat = "[起始位置],[长度] - 例如: 0,4表示从第0位置开始读取4个字节"
                    }
                },
                {
                    "Int",
                    new AddressDefinitionInfo
                    {
                        Description = "整数",
                        DataType = DataTypeEnum.Int32,
                        AddressFormat = "[起始位置] - 例如: 4表示从第4位置开始读取一个整数(4字节)"
                    }
                },
                {
                    "Float",
                    new AddressDefinitionInfo
                    {
                        Description = "浮点数",
                        DataType = DataTypeEnum.Float,
                        AddressFormat = "[起始位置] - 例如: 8表示从第8位置开始读取一个浮点数(4字节)"
                    }
                },
                {
                    "String",
                    new AddressDefinitionInfo
                    {
                        Description = "字符串",
                        DataType = DataTypeEnum.Utf8String,
                        AddressFormat = "[起始位置],[长度] - 例如: 12,10表示从第12位置开始读取10个字节的字符串"
                    }
                },
                {
                    "Bool",
                    new AddressDefinitionInfo
                    {
                        Description = "布尔值",
                        DataType = DataTypeEnum.Bool,
                        AddressFormat = "[起始位置].[位] - 例如: 22.0表示从第22位置的第0位"
                    }
                }
            };
        }
    }
}
