using System.Collections.Generic;
using PluginInterface;

namespace TCP.DKScrew.Models
{
    public static class DKScrewAddressDefinitions
    {
        public static Dictionary<string, AddressDefinitionInfo> GetDefinitions()
        {
            return new Dictionary<string, AddressDefinitionInfo>
            {
                // 系统状态
                {
                    "IsConnected",
                    new AddressDefinitionInfo
                    {
                        Description = "连接状态",
                        DataType = DataTypeEnum.Bool,
                        AddressFormat = "IsConnected"
                    }
                },
                {
                    "RunStatus",
                    new AddressDefinitionInfo
                    {
                        Description = "运行状态",
                        DataType = DataTypeEnum.Int32,
                        AddressFormat = "RunStatus"
                    }
                },
                {
                    "ErrorCode",
                    new AddressDefinitionInfo
                    {
                        Description = "错误代码",
                        DataType = DataTypeEnum.Int32,
                        AddressFormat = "ErrorCode"
                    }
                },

                // 拧紧结果
                {
                    "FinalTorque",
                    new AddressDefinitionInfo
                    {
                        Description = "最终扭矩",
                        DataType = DataTypeEnum.Float,
                        AddressFormat = "FinalTorque"
                    }
                },
                {
                    "FinalAngle",
                    new AddressDefinitionInfo
                    {
                        Description = "最终角度",
                        DataType = DataTypeEnum.Float,
                        AddressFormat = "FinalAngle"
                    }
                },
                {
                    "TighteningStatus",
                    new AddressDefinitionInfo
                    {
                        Description = "拧紧状态",
                        DataType = DataTypeEnum.Int32,
                        AddressFormat = "TighteningStatus"
                    }
                },

                // 实时数据
                {
                    "CurrentTorque",
                    new AddressDefinitionInfo
                    {
                        Description = "当前扭矩",
                        DataType = DataTypeEnum.Float,
                        AddressFormat = "CurrentTorque"
                    }
                },
                {
                    "CurrentAngle",
                    new AddressDefinitionInfo
                    {
                        Description = "当前角度",
                        DataType = DataTypeEnum.Float,
                        AddressFormat = "CurrentAngle"
                    }
                },
                {
                    "CurrentSpeed",
                    new AddressDefinitionInfo
                    {
                        Description = "当前速度",
                        DataType = DataTypeEnum.Float,
                        AddressFormat = "CurrentSpeed"
                    }
                },

                // 控制命令
                {
                    "StartMotor",
                    new AddressDefinitionInfo
                    {
                        Description = "启动电机",
                        DataType = DataTypeEnum.Bool,
                        AddressFormat = "StartMotor"
                    }
                },
                {
                    "StopMotor",
                    new AddressDefinitionInfo
                    {
                        Description = "停止电机",
                        DataType = DataTypeEnum.Bool,
                        AddressFormat = "StopMotor"
                    }
                },
                {
                    "SelectPset",
                    new AddressDefinitionInfo
                    {
                        Description = "选择程序号",
                        DataType = DataTypeEnum.Int32,
                        AddressFormat = "SelectPset"
                    }
                }
            };
        }
    }
}
