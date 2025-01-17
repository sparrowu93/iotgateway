using System.Web;

namespace IoTGateway.Model.Utils
{
    public static class HtmlEntityHelper
    {
        public static string DecodeHtmlEntities(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            return HttpUtility.HtmlDecode(input);
        }

        public static string EncodeHtmlEntities(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            return HttpUtility.HtmlEncode(input);
        }
    }
}
