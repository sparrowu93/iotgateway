using System.ComponentModel;
// Define these enums at the bottom of the file or in a Models folder
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
    [Description("form")]
    Form,
    
    [Description("x-www-form-urlencoded")]
    FormUrlEncoded,
    
    [Description("application/json")]
    Json,
    
    [Description("application/xml")]
    Xml,
    
    [Description("text/plain")]
    Raw
}