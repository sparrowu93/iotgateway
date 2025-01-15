using System.Collections.Generic;
using PluginInterface;

namespace OPC.DaClient.Models
{
    public static class DaClientAddressDefinitions
    {
        public static Dictionary<string, AddressDefinitionInfo> GetDefinitions()
        {
            return new Dictionary<string, AddressDefinitionInfo>
            {
                {
                    "Bool",
                    new AddressDefinitionInfo
                    {
                        Description = "布尔类型",
                        DataType = DataTypeEnum.Bool,
                        AddressFormat = "<组名>.<项名> - 例如: PLC.Switch1"
                    }
                },
                {
                    "Int16",
                    new AddressDefinitionInfo
                    {
                        Description = "16位整数",
                        DataType = DataTypeEnum.Int16,
                        AddressFormat = "<组名>.<项名> - 例如: PLC.Counter"
                    }
                },
                {
                    "Int32",
                    new AddressDefinitionInfo
                    {
                        Description = "32位整数",
                        DataType = DataTypeEnum.Int32,
                        AddressFormat = "<组名>.<项名> - 例如: PLC.Value"
                    }
                },
                {
                    "Float",
                    new AddressDefinitionInfo
                    {
                        Description = "单精度浮点数",
                        DataType = DataTypeEnum.Float,
                        AddressFormat = "<组名>.<项名> - 例如: PLC.Temperature"
                    }
                },
                {
                    "Double",
                    new AddressDefinitionInfo
                    {
                        Description = "双精度浮点数",
                        DataType = DataTypeEnum.Double,
                        AddressFormat = "<组名>.<项名> - 例如: PLC.Pressure"
                    }
                },
                {
                    "String",
                    new AddressDefinitionInfo
                    {
                        Description = "字符串",
                        DataType = DataTypeEnum.Utf8String,
                        AddressFormat = "<组名>.<项名> - 例如: PLC.Status"
                    }
                }
            };
        }
    }
}
