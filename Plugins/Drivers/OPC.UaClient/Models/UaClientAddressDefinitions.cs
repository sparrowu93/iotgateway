using System.Collections.Generic;
using PluginInterface;

namespace OPC.UaClient.Models
{
    public static class UaClientAddressDefinitions
    {
        public static Dictionary<string, AddressDefinitionInfo> GetDefinitions()
        {
            return new Dictionary<string, AddressDefinitionInfo>
            {
                {
                    "字符串标识符",
                    new AddressDefinitionInfo
                    {
                        Description = "使用字符串标识符的节点",
                        DataType = DataTypeEnum.Any,
                        AddressFormat = "ns=<命名空间索引>;s=<字符串标识符> - 例如: ns=2;s=通道1.设备1.标签1"
                    }
                },
                {
                    "数字标识符",
                    new AddressDefinitionInfo
                    {
                        Description = "使用数字标识符的节点",
                        DataType = DataTypeEnum.Any,
                        AddressFormat = "ns=<命名空间索引>;i=<数字标识符> - 例如: ns=0;i=2258"
                    }
                },
                {
                    "GUID标识符",
                    new AddressDefinitionInfo
                    {
                        Description = "使用GUID标识符的节点",
                        DataType = DataTypeEnum.Any,
                        AddressFormat = "ns=<命名空间索引>;g=<GUID> - 例如: ns=1;g=09087e75-8e5e-499b-954f-f2a8624db28a"
                    }
                },
                {
                    "二进制标识符",
                    new AddressDefinitionInfo
                    {
                        Description = "使用二进制标识符的节点",
                        DataType = DataTypeEnum.Any,
                        AddressFormat = "ns=<命名空间索引>;b=<Base64字符串> - 例如: ns=1;b=SGVsbG8gV29ybGQ="
                    }
                },
                {
                    "相对路径",
                    new AddressDefinitionInfo
                    {
                        Description = "使用相对路径的节点",
                        DataType = DataTypeEnum.Any,
                        AddressFormat = "ns=<命名空间索引>;s=<起始节点>/<子节点> - 例如: ns=2;s=设备/温度/当前值"
                    }
                },
                {
                    "服务器时间",
                    new AddressDefinitionInfo
                    {
                        Description = "OPC UA服务器时间节点",
                        DataType = DataTypeEnum.DateTime,
                        AddressFormat = "ns=0;i=2258"
                    }
                }
            };
        }
    }
}
