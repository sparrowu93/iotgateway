using System.Collections.Generic;

namespace PluginInterface
{
    public interface IAddressDefinitionProvider
    {
        Dictionary<string, AddressDefinitionInfo> GetAddressDefinitions();
    }

    public class AddressDefinitionInfo
    {
        public string Description { get; set; }
        public DataTypeEnum DataType { get; set; }
        public string Unit { get; set; }
        public string AddressFormat { get; set; }
    }
}
