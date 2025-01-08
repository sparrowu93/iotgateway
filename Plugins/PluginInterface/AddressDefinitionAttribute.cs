using System;

namespace PluginInterface
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public class AddressDefinitionAttribute : Attribute
    {
        public string Description { get; }
        public DataTypeEnum DataType { get; }
        public string Unit { get; }
        public string AddressFormat { get; }

        public AddressDefinitionAttribute(string description, DataTypeEnum dataType, string addressFormat = "", string unit = "")
        {
            Description = description;
            DataType = dataType;
            AddressFormat = addressFormat;
            Unit = unit;
        }
    }
}
