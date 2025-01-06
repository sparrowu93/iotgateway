using Newtonsoft.Json;

namespace MQTT.Driver.Models
{
    public class JsonDataModel
    {
        [JsonProperty("value")]
        public double Value { get; set; }

        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonProperty("quality")]
        public string Quality { get; set; }

        [JsonProperty("tags")]
        public Dictionary<string, object> Tags { get; set; }
    }
}
