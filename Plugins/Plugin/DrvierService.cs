using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PluginInterface;
using System.Reflection;
using System.Text.Json;
using WalkingTec.Mvvm.Core;
using IoTGateway.DataAccess;
using IoTGateway.Model;
using Microsoft.Extensions.Logging;

namespace Plugin
{
    public class DriverService
    {
        private readonly ILogger<DriverService> _logger;
        readonly string _driverPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"drivers/net6.0");
        readonly string[] _driverFiles;
        public List<DriverInfo> DriverInfos = new();

        public DriverService(IConfiguration configRoot, ILogger<DriverService> logger)
        {
            _logger = logger;
            try
            {
                _logger.LogInformation("LoadDriverFiles Start");
                _driverFiles = Directory.GetFiles(_driverPath).Where(x => Path.GetExtension(x) == ".dll").ToArray();
                _logger.LogInformation($"LoadDriverFiles End，Count{_driverFiles.Count()}");
            }
            catch (Exception ex)
            {
                _logger.LogError("LoadDriverFiles Error", ex);
            }

            LoadAllDrivers();
        }

        public List<ComboSelectListItem> GetAllDrivers()
        {
            List<ComboSelectListItem> driverFilesComboSelect = new List<ComboSelectListItem>();
            using var dc = new DataContext(IoTBackgroundService.connnectSetting, IoTBackgroundService.DbType);
            var drivers = dc.Set<Driver>().AsNoTracking().ToList();

            foreach (var file in _driverFiles)
            {
                var dll = Assembly.LoadFrom(file);
                if (dll.GetTypes().Where(x => typeof(IDriver).IsAssignableFrom(x) && x.IsClass).Any())
                {
                    var fileName = Path.GetFileName(file);
                    var item = new ComboSelectListItem
                    {
                        Text = fileName,
                        Value = fileName,
                        Disabled = false,
                    };
                    if (drivers.Where(x => x.FileName == Path.GetFileName(file)).Any())
                        item.Disabled = true;
                    driverFilesComboSelect.Add(item);
                }
            }

            return driverFilesComboSelect;
        }

        public (string AssembleName, string ErrorMessage) GetAssembleNameByFileName(string fileName)
        {
            try
            {
                if (string.IsNullOrEmpty(fileName))
                {
                    return (null, "驱动文件名不能为空");
                }

                var file = _driverFiles.SingleOrDefault(f => Path.GetFileName(f) == fileName);
                if (file == null)
                {
                    return (null, $"驱动文件 {fileName} 不存在，请确保文件已复制到drivers/net6.0目录");
                }

                _logger.LogDebug("正在加载驱动程序集: {file}", file);
                var dll = Assembly.LoadFrom(file);
                var type = dll.GetTypes().FirstOrDefault(x => typeof(IDriver).IsAssignableFrom(x) && x.IsClass);
                
                if (type == null)
                {
                    return (null, $"驱动文件 {fileName} 中未找到实现IDriver接口的类");
                }

                _logger.LogDebug("找到驱动类型: {typeName}", type.FullName);
                return (type.FullName, null);
            }
            catch (BadImageFormatException)
            {
                return (null, $"驱动文件 {fileName} 不是有效的.NET程序集，请确保编译目标平台正确");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载驱动程序集时出错: {fileName}", fileName);
                return (null, $"加载驱动失败: {ex.Message}");
            }
        }

        public void AddConfigs(Guid? dapId, Guid? driverId)
        {
            using var dc = new DataContext(IoTBackgroundService.connnectSetting, IoTBackgroundService.DbType);
            var device = dc.Set<Device>().Where(x => x.ID == dapId).AsNoTracking().SingleOrDefault();
            var driver = dc.Set<Driver>().Where(x => x.ID == driverId).AsNoTracking().SingleOrDefault();
            var type = DriverInfos.SingleOrDefault(x => x.Type.FullName == driver?.AssembleName);

            Type[] types = { typeof(string), typeof(ILogger) };
            object[] param = { device?.DeviceName, _logger };

            ConstructorInfo? constructor = type?.Type.GetConstructor(types);
            var iObj = constructor?.Invoke(param) as IDriver;

            foreach (var property in type?.Type.GetProperties())
            {
                var config = property.GetCustomAttribute(typeof(ConfigParameterAttribute));
                if (config != null)
                {
                    var dapConfig = new DeviceConfig
                    {
                        ID = Guid.NewGuid(),
                        DeviceId = dapId,
                        DeviceConfigName = property.Name,
                        DataSide = DataSide.AnySide,
                        Description = ((ConfigParameterAttribute)config).Description,
                        Value = property.GetValue(iObj)?.ToString()
                    };

                    if (property.PropertyType.BaseType == typeof(Enum))
                    {
                        var fields = property.PropertyType.GetFields(BindingFlags.Static | BindingFlags.Public);
                        var enumInfos = fields.ToDictionary(f => f.Name, f => (int)f.GetValue(null));
                        dapConfig.EnumInfo = JsonSerializer.Serialize(enumInfos);
                    }

                    dc.Set<DeviceConfig>().Add(dapConfig);
                }
            }

            dc.SaveChanges();
        }

        public void LoadAllDrivers()
        {
            _logger.LogInformation("LoadAllDrivers Start");
            foreach (var file in _driverFiles)
            {
                try
                {
                    var dll = Assembly.LoadFrom(file);
                    foreach (var type in dll.GetTypes().Where(x => typeof(IDriver).IsAssignableFrom(x) && x.IsClass))
                    {
                        DriverInfo driverInfo = new DriverInfo
                        {
                            FileName = Path.GetFileName(file),
                            Type = type
                        };
                        DriverInfos.Add(driverInfo);
                        _logger.LogInformation($"LoadAllDrivers {driverInfo.FileName} OK");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"LoadAllDrivers Error {file}");
                }
            };

            _logger.LogInformation($"LoadAllDrivers End,Count{DriverInfos.Count}");
        }

        public void LoadRegestedDeviers()
        {
            using var dc = new DataContext(IoTBackgroundService.connnectSetting, IoTBackgroundService.DbType);

            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"drivers/net6.0");
            var files = Directory.GetFiles(path).Where(x => Path.GetExtension(x) == ".dll").ToArray();
            foreach (var file in files)
            {
                var dll = Assembly.LoadFrom(file);
                foreach (var type in dll.GetTypes().Where(x => typeof(IDriver).IsAssignableFrom(x) && x.IsClass))
                {
                    DriverInfo driverInfo = new DriverInfo
                    {
                        FileName = Path.GetFileName(file),
                        Type = type
                    };
                    DriverInfos.Add(driverInfo);
                }
            }
        }

        public Dictionary<string, AddressDefinitionInfo> GetDriverAddressDefinitions(string driverDll)
        {
            var driver = GetDriverInstance(driverDll);
            if (driver is IAddressDefinitionProvider provider)
            {
                return provider.GetAddressDefinitions();
            }
            
            // If driver doesn't implement IAddressDefinitionProvider, try to get definitions from attributes
            var definitions = new Dictionary<string, AddressDefinitionInfo>();
            var type = driver.GetType();
            var properties = type.GetProperties();
            
            foreach (var property in properties)
            {
                var attr = property.GetCustomAttribute<AddressDefinitionAttribute>();
                if (attr != null)
                {
                    definitions[property.Name] = new AddressDefinitionInfo
                    {
                        Description = attr.Description,
                        DataType = attr.DataType,
                        Unit = attr.Unit,
                        AddressFormat = attr.AddressFormat
                    };
                }
            }
            
            return definitions;
        }

        private IDriver GetDriverInstance(string driverDll)
        {
            var assembly = Assembly.LoadFrom(Path.Combine(_driverPath, driverDll));
            var driverType = assembly.GetTypes()
                .FirstOrDefault(t => typeof(IDriver).IsAssignableFrom(t) && t.IsClass);
                
            if (driverType == null)
                throw new Exception($"No valid driver found in {driverDll}");
                
            return (IDriver)Activator.CreateInstance(driverType);
        }
    }
}