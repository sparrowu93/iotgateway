using System.Collections.Generic;
using PluginInterface;

namespace HTTP.JsonClient.Models
{
    public static class HttpJsonAddressDefinitions
    {
        public static Dictionary<string, AddressDefinitionInfo> GetDefinitions()
        {
            return new Dictionary<string, AddressDefinitionInfo>
            {
                {
                    "JsonPath",
                    new AddressDefinitionInfo
                    {
                        Description = "JSON路径",
                        DataType = DataTypeEnum.Utf8String,
                        AddressFormat = "$.path.to.value - 使用JsonPath语法访问JSON数据，例如: $.data.temperature"
                    }
                },
                {
                    "JsonPathInt",
                    new AddressDefinitionInfo
                    {
                        Description = "JSON路径(整数)",
                        DataType = DataTypeEnum.Int32,
                        AddressFormat = "$.path.to.value - 返回整数类型，例如: $.data.count"
                    }
                },
                {
                    "JsonPathFloat",
                    new AddressDefinitionInfo
                    {
                        Description = "JSON路径(浮点数)",
                        DataType = DataTypeEnum.Float,
                        AddressFormat = "$.path.to.value - 返回浮点数类型，例如: $.data.temperature"
                    }
                },
                {
                    "JsonPathBool",
                    new AddressDefinitionInfo
                    {
                        Description = "JSON路径(布尔值)",
                        DataType = DataTypeEnum.Bool,
                        AddressFormat = "$.path.to.value - 返回布尔类型，例如: $.data.isEnabled"
                    }
                },
                {
                    "JsonPathArray",
                    new AddressDefinitionInfo
                    {
                        Description = "JSON路径(数组)",
                        DataType = DataTypeEnum.Utf8String,
                        AddressFormat = "$.path.to.array[index] - 访问数组元素，例如: $.data.items[0]"
                    }
                },
                {
                    "LastResponse",
                    new AddressDefinitionInfo
                    {
                        Description = "最近一次响应",
                        DataType = DataTypeEnum.Utf8String,
                        AddressFormat = "LastResponse - 获取最近一次HTTP请求的完整响应内容"
                    }
                },
                {
                    "StatusCode",
                    new AddressDefinitionInfo
                    {
                        Description = "状态码",
                        DataType = DataTypeEnum.Int32,
                        AddressFormat = "StatusCode - 获取最近一次HTTP请求的状态码"
                    }
                },
                {
                    "IsConnected",
                    new AddressDefinitionInfo
                    {
                        Description = "连接状态",
                        DataType = DataTypeEnum.Bool,
                        AddressFormat = "IsConnected - 获取当前连接状态"
                    }
                }
            };
        }
    }
}
