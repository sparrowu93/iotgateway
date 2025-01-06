using System;
using System.Text;
using MQTT.Driver.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MQTT.Driver.Tests
{
    public class JsonParserTests
    {
        [Fact]
        public void TestBasicJsonParsing()
        {
            // Arrange
            string jsonData = @"{
                'value': 23.5,
                'timestamp': '2025-01-06T14:37:36+08:00',
                'quality': 'GOOD',
                'tags': {
                    'unit': 'celsius',
                    'location': 'sensor1'
                }
            }";

            // Act
            var data = JsonConvert.DeserializeObject<JsonDataModel>(jsonData);

            // Assert
            Assert.NotNull(data);
            Assert.Equal(23.5, data.Value);
            Assert.Equal(DateTime.Parse("2025-01-06T14:37:36+08:00"), data.Timestamp);
            Assert.Equal("GOOD", data.Quality);
            Assert.Equal("celsius", data.Tags["unit"]);
            Assert.Equal("sensor1", data.Tags["location"]);
        }

        [Fact]
        public void TestNestedJsonParsing()
        {
            // Arrange
            string jsonData = @"{
                'value': {
                    'temperature': 23.5,
                    'humidity': 45.2
                },
                'timestamp': '2025-01-06T14:37:36+08:00',
                'quality': 'GOOD',
                'tags': {
                    'device': 'environmental_sensor',
                    'location': 'room1'
                }
            }";

            // Act
            var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonData);

            // Assert
            Assert.NotNull(data);
            var valueObj = (Newtonsoft.Json.Linq.JObject)data["value"];
            Assert.Equal(23.5, valueObj.Value<double>("temperature"));
            Assert.Equal(45.2, valueObj.Value<double>("humidity"));
            Assert.Equal(DateTime.Parse("2025-01-06T14:37:36+08:00"), (DateTime)data["timestamp"]);
            Assert.Equal("GOOD", (string)data["quality"]);
            var tags = (Newtonsoft.Json.Linq.JObject)data["tags"];
            Assert.Equal("environmental_sensor", tags.Value<string>("device"));
            Assert.Equal("room1", tags.Value<string>("location"));
        }

        [Fact]
        public void TestInvalidJsonParsing()
        {
            // Arrange
            string invalidJsonData = @"{
                'value': 'not_a_number',
                'timestamp': '2025-01-06T14:37:36+08:00',
                'quality': 'GOOD',
                'tags': {}
            }";

            // Act & Assert
            Assert.Throws<JsonReaderException>(() => 
                JsonConvert.DeserializeObject<JsonDataModel>(invalidJsonData));
        }

        [Theory]
        [InlineData(23.5, "GOOD")]
        [InlineData(0, "BAD")]
        [InlineData(-273.15, "UNCERTAIN")]
        public void TestMultipleDataValues(double value, string quality)
        {
            // Arrange
            var jsonData = $@"{{
                'value': {value},
                'timestamp': '2025-01-06T14:37:36+08:00',
                'quality': '{quality}',
                'tags': {{}}
            }}";

            // Act
            var data = JsonConvert.DeserializeObject<JsonDataModel>(jsonData);

            // Assert
            Assert.NotNull(data);
            Assert.Equal(value, data.Value);
            Assert.Equal(quality, data.Quality);
        }
    }
}
