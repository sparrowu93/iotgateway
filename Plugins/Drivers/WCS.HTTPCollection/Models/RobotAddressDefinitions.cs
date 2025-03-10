using System.Collections.Generic;
using PluginInterface;

namespace Robot.DataCollector.Models
{
    public static class RobotAddressDefinitions
    {
        public static Dictionary<string, AddressDefinitionInfo> GetDefinitions()
        {
            return new Dictionary<string, AddressDefinitionInfo>
            {
                {
                    "RobotState",
                    new AddressDefinitionInfo
                    {
                        Description = "机器人状态JSON数据",
                        DataType = DataTypeEnum.Utf8String,
                        AddressFormat = "$.path.to.value - 使用JsonPath语法访问机器人状态数据，例如: $.Data.RobotsState[0].Battery.Capacity"
                    }
                },
                {
                    "RobotStateInt",
                    new AddressDefinitionInfo
                    {
                        Description = "机器人状态整数值",
                        DataType = DataTypeEnum.Int32,
                        AddressFormat = "$.path.to.value - 返回整数类型，例如: $.Data.RobotsState[0].State"
                    }
                },
                {
                    "RobotStateFloat",
                    new AddressDefinitionInfo
                    {
                        Description = "机器人状态浮点数值",
                        DataType = DataTypeEnum.Float,
                        AddressFormat = "$.path.to.value - 返回浮点数类型，例如: $.Data.RobotsState[0].Battery.Capacity"
                    }
                },
                {
                    "RobotStateBool",
                    new AddressDefinitionInfo
                    {
                        Description = "机器人状态布尔值",
                        DataType = DataTypeEnum.Bool,
                        AddressFormat = "$.path.to.value - 返回布尔类型，例如: $.Data.RobotsState[0].SoftEmer"
                    }
                },
                {
                    "TaskNotification",
                    new AddressDefinitionInfo
                    {
                        Description = "任务通知JSON数据",
                        DataType = DataTypeEnum.Utf8String,
                        AddressFormat = "任务通知数据，例如: $.Id 返回任务ID"
                    }
                },
                {
                    "TaskConfig",
                    new AddressDefinitionInfo
                    {
                        Description = "任务配置JSON数据",
                        DataType = DataTypeEnum.Utf8String,
                        AddressFormat = "任务配置数据，例如: $.Enable 返回是否启用任务通知"
                    }
                }
            };
        }
    }
}
