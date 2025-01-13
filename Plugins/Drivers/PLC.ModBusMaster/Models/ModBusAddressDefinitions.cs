using System.Collections.Generic;
using PluginInterface;

namespace PLC.ModBusMaster.Models
{
    public static class ModBusAddressDefinitions
    {
        public static Dictionary<string, AddressDefinitionInfo> GetDefinitions()
        {
            return new Dictionary<string, AddressDefinitionInfo>
            {
                // 功能码01 - 线圈
                {
                    "01",
                    new AddressDefinitionInfo
                    {
                        Description = "线圈状态(读写)",
                        DataType = DataTypeEnum.Bool,
                        AddressFormat = "[站号]|[地址] - 例如: 1|0表示站号1的0号线圈，站号可选"
                    }
                },

                // 功能码02 - 输入状态
                {
                    "02",
                    new AddressDefinitionInfo
                    {
                        Description = "输入状态(只读)",
                        DataType = DataTypeEnum.Bool,
                        AddressFormat = "[站号]|[地址] - 例如: 1|100表示站号1的100号输入，站号可选"
                    }
                },

                // 功能码03 - 保持寄存器
                {
                    "03-Int16",
                    new AddressDefinitionInfo
                    {
                        Description = "保持寄存器(读写) - 16位整数",
                        DataType = DataTypeEnum.Int16,
                        AddressFormat = "[站号]|[地址] - 例如: 1|0表示站号1的0号寄存器，站号可选"
                    }
                },
                {
                    "03-UInt16",
                    new AddressDefinitionInfo
                    {
                        Description = "保持寄存器(读写) - 16位无符号整数",
                        DataType = DataTypeEnum.Uint16,
                        AddressFormat = "[站号]|[地址] - 例如: 1|0表示站号1的0号寄存器，站号可选"
                    }
                },
                {
                    "03-Int32",
                    new AddressDefinitionInfo
                    {
                        Description = "保持寄存器(读写) - 32位整数",
                        DataType = DataTypeEnum.Int32,
                        AddressFormat = "[站号]|[地址] - 例如: 1|0表示站号1的0号寄存器，占用2个寄存器，站号可选"
                    }
                },
                {
                    "03-UInt32",
                    new AddressDefinitionInfo
                    {
                        Description = "保持寄存器(读写) - 32位无符号整数",
                        DataType = DataTypeEnum.Uint32,
                        AddressFormat = "[站号]|[地址] - 例如: 1|0表示站号1的0号寄存器，占用2个寄存器，站号可选"
                    }
                },
                {
                    "03-Float",
                    new AddressDefinitionInfo
                    {
                        Description = "保持寄存器(读写) - 浮点数",
                        DataType = DataTypeEnum.Float,
                        AddressFormat = "[站号]|[地址] - 例如: 1|0表示站号1的0号寄存器，占用2个寄存器，站号可选"
                    }
                },
                {
                    "03-String",
                    new AddressDefinitionInfo
                    {
                        Description = "保持寄存器(读写) - ASCII字符串",
                        DataType = DataTypeEnum.AsciiString,
                        AddressFormat = "[站号]|[地址],[长度] - 例如: 1|0,10表示站号1从0号寄存器开始读取10个字符，站号可选"
                    }
                },

                // 功能码04 - 输入寄存器
                {
                    "04-Int16",
                    new AddressDefinitionInfo
                    {
                        Description = "输入寄存器(只读) - 16位整数",
                        DataType = DataTypeEnum.Int16,
                        AddressFormat = "[站号]|[地址] - 例如: 1|0表示站号1的0号输入寄存器，站号可选"
                    }
                },
                {
                    "04-UInt16",
                    new AddressDefinitionInfo
                    {
                        Description = "输入寄存器(只读) - 16位无符号整数",
                        DataType = DataTypeEnum.Uint16,
                        AddressFormat = "[站号]|[地址] - 例如: 1|0表示站号1的0号输入寄存器，站号可选"
                    }
                },
                {
                    "04-Int32",
                    new AddressDefinitionInfo
                    {
                        Description = "输入寄存器(只读) - 32位整数",
                        DataType = DataTypeEnum.Int32,
                        AddressFormat = "[站号]|[地址] - 例如: 1|0表示站号1的0号输入寄存器，占用2个寄存器，站号可选"
                    }
                },
                {
                    "04-UInt32",
                    new AddressDefinitionInfo
                    {
                        Description = "输入寄存器(只读) - 32位无符号整数",
                        DataType = DataTypeEnum.Uint32,
                        AddressFormat = "[站号]|[地址] - 例如: 1|0表示站号1的0号输入寄存器，占用2个寄存器，站号可选"
                    }
                },
                {
                    "04-Float",
                    new AddressDefinitionInfo
                    {
                        Description = "输入寄存器(只读) - 浮点数",
                        DataType = DataTypeEnum.Float,
                        AddressFormat = "[站号]|[地址] - 例如: 1|0表示站号1的0号输入寄存器，占用2个寄存器，站号可选"
                    }
                },
                {
                    "04-String",
                    new AddressDefinitionInfo
                    {
                        Description = "输入寄存器(只读) - ASCII字符串",
                        DataType = DataTypeEnum.AsciiString,
                        AddressFormat = "[站号]|[地址],[长度] - 例如: 1|0,10表示站号1从0号输入寄存器开始读取10个字符，站号可选"
                    }
                },

                // 批量读取
                {
                    "BatchRead",
                    new AddressDefinitionInfo
                    {
                        Description = "批量读取",
                        DataType = DataTypeEnum.Utf8String,
                        AddressFormat = "[站号]|[功能码],[起始地址],[长度],[缓存名] - 例如: 1|3,0,10,cache1 表示从站号1读取10个保持寄存器到cache1，站号可选"
                    }
                },

                // 从缓存读取
                {
                    "ReadCache",
                    new AddressDefinitionInfo
                    {
                        Description = "从缓存读取",
                        DataType = DataTypeEnum.Utf8String,
                        AddressFormat = "[缓存名],[偏移地址] - 例如: cache1,2 表示从cache1缓存的第2个位置读取数据"
                    }
                }
            };
        }
    }
}
