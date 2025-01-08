using PluginInterface;
using System.Collections.Generic;

namespace MQTT.Driver.Models
{
    public static class MqttAddressDefinitions
    {
        // Common MQTT Topics
        public const string SYSTEM_STATUS = "system/status";
        public const string DEVICE_INFO = "device/info";
        public const string TELEMETRY = "telemetry";
        public const string CONTROL = "control";
        
        // Common JSON Paths
        public const string JSON_VALUE = "$.value";
        public const string JSON_TIMESTAMP = "$.timestamp";
        public const string JSON_STATUS = "$.status";
        public const string JSON_BATTERY = "$.battery";
        public const string JSON_TEMPERATURE = "$.temperature";
        public const string JSON_HUMIDITY = "$.humidity";
        
        public static Dictionary<string, AddressDefinitionInfo> GetDefinitions()
        {
            return new Dictionary<string, AddressDefinitionInfo>
            {
                // System Status
                [$"{SYSTEM_STATUS}{JSON_STATUS}"] = new AddressDefinitionInfo
                {
                    Description = "系统状态",
                    DataType = DataTypeEnum.Int32,
                    AddressFormat = "system/status.$.status"
                },
                
                // Device Info
                [$"{DEVICE_INFO}{JSON_BATTERY}"] = new AddressDefinitionInfo
                {
                    Description = "电池电量",
                    DataType = DataTypeEnum.Float,
                    Unit = "%",
                    AddressFormat = "device/info.$.battery"
                },
                
                // Telemetry
                [$"{TELEMETRY}{JSON_TEMPERATURE}"] = new AddressDefinitionInfo
                {
                    Description = "温度",
                    DataType = DataTypeEnum.Float,
                    Unit = "°C",
                    AddressFormat = "telemetry.$.temperature"
                },
                
                [$"{TELEMETRY}{JSON_HUMIDITY}"] = new AddressDefinitionInfo
                {
                    Description = "湿度",
                    DataType = DataTypeEnum.Float,
                    Unit = "%",
                    AddressFormat = "telemetry.$.humidity"
                },
                
                [$"{TELEMETRY}{JSON_VALUE}"] = new AddressDefinitionInfo
                {
                    Description = "通用数值",
                    DataType = DataTypeEnum.Float,
                    AddressFormat = "telemetry.$.value"
                },
                
                [$"{TELEMETRY}{JSON_TIMESTAMP}"] = new AddressDefinitionInfo
                {
                    Description = "时间戳",
                    DataType = DataTypeEnum.Int64,
                    AddressFormat = "telemetry.$.timestamp"
                },
                
                // Control
                [$"{CONTROL}{JSON_VALUE}"] = new AddressDefinitionInfo
                {
                    Description = "控制值",
                    DataType = DataTypeEnum.Float,
                    AddressFormat = "control.$.value"
                }
            };
        }
    }
}
