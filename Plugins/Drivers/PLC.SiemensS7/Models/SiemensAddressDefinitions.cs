using System.Collections.Generic;
using PluginInterface;

namespace PLC.SiemensS7.Models
{
    public static class SiemensAddressDefinitions
    {
        public static Dictionary<string, AddressDefinitionInfo> GetDefinitions()
        {
            return new Dictionary<string, AddressDefinitionInfo>
            {
                // 输入区域 (I)
                {
                    "I",
                    new AddressDefinitionInfo
                    {
                        Description = "输入区",
                        DataType = DataTypeEnum.Bool,
                        AddressFormat = "I[byte].[bit] - 例如: I0.0表示输入区第0字节第0位"
                    }
                },
                {
                    "IB",
                    new AddressDefinitionInfo
                    {
                        Description = "输入字节",
                        DataType = DataTypeEnum.Byte,
                        AddressFormat = "IB[byte] - 例如: IB1表示输入区第1字节"
                    }
                },
                {
                    "IW",
                    new AddressDefinitionInfo
                    {
                        Description = "输入字",
                        DataType = DataTypeEnum.Int16,
                        AddressFormat = "IW[byte] - 例如: IW2表示输入区第2字节开始的字"
                    }
                },
                {
                    "ID",
                    new AddressDefinitionInfo
                    {
                        Description = "输入双字",
                        DataType = DataTypeEnum.Int32,
                        AddressFormat = "ID[byte] - 例如: ID4表示输入区第4字节开始的双字"
                    }
                },

                // 输出区域 (Q)
                {
                    "Q",
                    new AddressDefinitionInfo
                    {
                        Description = "输出区",
                        DataType = DataTypeEnum.Bool,
                        AddressFormat = "Q[byte].[bit] - 例如: Q0.0表示输出区第0字节第0位"
                    }
                },
                {
                    "QB",
                    new AddressDefinitionInfo
                    {
                        Description = "输出字节",
                        DataType = DataTypeEnum.Byte,
                        AddressFormat = "QB[byte] - 例如: QB1表示输出区第1字节"
                    }
                },
                {
                    "QW",
                    new AddressDefinitionInfo
                    {
                        Description = "输出字",
                        DataType = DataTypeEnum.Int16,
                        AddressFormat = "QW[byte] - 例如: QW2表示输出区第2字节开始的字"
                    }
                },
                {
                    "QD",
                    new AddressDefinitionInfo
                    {
                        Description = "输出双字",
                        DataType = DataTypeEnum.Int32,
                        AddressFormat = "QD[byte] - 例如: QD4表示输出区第4字节开始的双字"
                    }
                },

                // 内部标记区域 (M)
                {
                    "M",
                    new AddressDefinitionInfo
                    {
                        Description = "内部标记位",
                        DataType = DataTypeEnum.Bool,
                        AddressFormat = "M[byte].[bit] - 例如: M0.0表示标记区第0字节第0位"
                    }
                },
                {
                    "MB",
                    new AddressDefinitionInfo
                    {
                        Description = "内部标记字节",
                        DataType = DataTypeEnum.Byte,
                        AddressFormat = "MB[byte] - 例如: MB1表示标记区第1字节"
                    }
                },
                {
                    "MW",
                    new AddressDefinitionInfo
                    {
                        Description = "内部标记字",
                        DataType = DataTypeEnum.Int16,
                        AddressFormat = "MW[byte] - 例如: MW2表示标记区第2字节开始的字"
                    }
                },
                {
                    "MD",
                    new AddressDefinitionInfo
                    {
                        Description = "内部标记双字",
                        DataType = DataTypeEnum.Int32,
                        AddressFormat = "MD[byte] - 例如: MD4表示标记区第4字节开始的双字"
                    }
                },

                // 数据块区域 (DB)
                {
                    "DB-Bit",
                    new AddressDefinitionInfo
                    {
                        Description = "数据块位",
                        DataType = DataTypeEnum.Bool,
                        AddressFormat = "DB[block].DBX[byte].[bit] - 例如: DB1.DBX0.0表示DB1第0字节第0位"
                    }
                },
                {
                    "DB-Byte",
                    new AddressDefinitionInfo
                    {
                        Description = "数据块字节",
                        DataType = DataTypeEnum.Byte,
                        AddressFormat = "DB[block].DBB[byte] - 例如: DB1.DBB1表示DB1第1字节"
                    }
                },
                {
                    "DB-Word",
                    new AddressDefinitionInfo
                    {
                        Description = "数据块字",
                        DataType = DataTypeEnum.Int16,
                        AddressFormat = "DB[block].DBW[byte] - 例如: DB1.DBW2表示DB1第2字节开始的字"
                    }
                },
                {
                    "DB-DWord",
                    new AddressDefinitionInfo
                    {
                        Description = "数据块双字",
                        DataType = DataTypeEnum.Int32,
                        AddressFormat = "DB[block].DBD[byte] - 例如: DB1.DBD4表示DB1第4字节开始的双字"
                    }
                },
                {
                    "DB-Float",
                    new AddressDefinitionInfo
                    {
                        Description = "数据块浮点数",
                        DataType = DataTypeEnum.Float,
                        AddressFormat = "DB[block].DBD[byte] - 例如: DB1.DBD8表示DB1第8字节开始的浮点数"
                    }
                },
                {
                    "DB-String",
                    new AddressDefinitionInfo
                    {
                        Description = "数据块字符串",
                        DataType = DataTypeEnum.Utf8String,
                        AddressFormat = "DB[block].DBW[byte],[length] - 例如: DB1.DBW6,10表示从DB1第6字节开始的10字节字符串"
                    }
                }
            };
        }
    }
}
