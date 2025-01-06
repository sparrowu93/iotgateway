using System.ComponentModel;
using Newtonsoft.Json;

namespace TCP.DKScrew.Models
{
    public class DKScrewDeviceConfig
    {
        [JsonProperty("ip")]
        [DisplayName("IP地址")]
        [Category("设备参数")]
        [Description("电批控制器的IP地址")]
        public string IpAddress { get; set; } = "127.0.0.1";

        [JsonProperty("port")]
        [DisplayName("端口")]
        [Category("设备参数")]
        [Description("电批控制器的端口号")]
        public int Port { get; set; } = 4001;

        [JsonProperty("timeout")]
        [DisplayName("超时时间")]
        [Category("通讯参数")]
        [Description("通讯超时时间(毫秒)")]
        public int Timeout { get; set; } = 3000;

        [JsonProperty("reconnectInterval")]
        [DisplayName("重连间隔")]
        [Category("通讯参数")]
        [Description("断线重连间隔时间(毫秒)")]
        public int ReconnectInterval { get; set; } = 5000;

        [JsonProperty("enableCurveData")]
        [DisplayName("启用曲线数据")]
        [Category("功能设置")]
        [Description("是否采集拧紧曲线数据")]
        public bool EnableCurveData { get; set; } = true;

        [JsonProperty("curveDataInterval")]
        [DisplayName("曲线采集间隔")]
        [Category("功能设置")]
        [Description("曲线数据采集间隔(毫秒)")]
        public int CurveDataInterval { get; set; } = 100;

        [JsonProperty("statusUpdateInterval")]
        [DisplayName("状态更新间隔")]
        [Category("功能设置")]
        [Description("设备状态更新间隔(毫秒)")]
        public int StatusUpdateInterval { get; set; } = 1000;

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this, Formatting.Indented);
        }
    }
}
