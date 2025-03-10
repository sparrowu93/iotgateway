using System.ComponentModel;

namespace Robot.DataCollector.Models
{
    public enum HttpMethod
    {
        GET,
        POST,
        PUT,
        DELETE,
        PATCH,
        HEAD,
        OPTIONS
    }

    public enum ContentType
    {
        [Description("application/json")]
        Json,
        
        [Description("application/xml")]
        Xml,
        
        [Description("application/x-www-form-urlencoded")]
        FormUrlEncoded,
        
        [Description("text/plain")]
        Raw
    }
}
