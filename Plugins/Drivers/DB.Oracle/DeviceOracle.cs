using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;
using PluginInterface;

namespace DB.Oracle
{
    [DriverInfo("Oracle数据库", "用于定期读取Oracle数据库中的数据", "1.0.0")]
    public class DeviceOracle : IDriver, IAddressDefinitionProvider, IDisposable
    {
        [ConfigParameter("设备Id")]
        public string DeviceId { get; set; }

        [ConfigParameter("数据库服务器地址")]
        public string ServerHost { get; set; } = "localhost";

        [ConfigParameter("数据库服务器端口")]
        public int ServerPort { get; set; } = 1521;

        [ConfigParameter("服务名/SID")]
        public string ServiceName { get; set; } = "ORCL";

        [ConfigParameter("数据库用户名")]
        public string Username { get; set; } = "system";

        [ConfigParameter("数据库密码")]
        public string Password { get; set; }

        [ConfigParameter("默认数据表名")]
        public string DefaultTableName { get; set; }

        [ConfigParameter("默认查询条件")]
        public string DefaultWhereClause { get; set; }

        [ConfigParameter("超时时间ms")]
        public int Timeout { get; set; } = 3000;

        [ConfigParameter("最小通讯周期ms")]
        public uint MinPeriod { get; set; } = 1000;

        public bool IsConnected { get; private set; }
        public ILogger _logger { get; set; }

        private OracleConnection _connection;
        private Dictionary<string, string> _cachedQueries = new();
        private Dictionary<string, DateTime> _lastQueryTimes = new();

        public DeviceOracle()
        {
            IsConnected = false;
        }

        private string BuildConnectionString()
        {
            return $"Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={ServerHost})(PORT={ServerPort}))" +
                   $"(CONNECT_DATA=(SERVICE_NAME={ServiceName})));User Id={Username};Password={Password};";
        }

        public bool Connect()
        {
            try
            {
                if (_connection != null)
                {
                    Close();
                }

                _connection = new OracleConnection(BuildConnectionString());
                _connection.Open();
                IsConnected = true;
                _logger?.LogInformation("Successfully connected to Oracle database");
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to connect to Oracle database");
                IsConnected = false;
                return false;
            }
        }

        public bool Close()
        {
            try
            {
                if (_connection != null)
                {
                    _connection.Close();
                    _connection.Dispose();
                    _connection = null;
                }
                IsConnected = false;
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error closing Oracle connection");
                return false;
            }
        }

        public void Dispose()
        {
            Close();
        }

        public DriverReturnValueModel Read(DriverAddressIoArgModel ioArg)
        {
            var returnValue = new DriverReturnValueModel { StatusType = VaribaleStatusTypeEnum.Good };

            try
            {
                if (!IsConnected)
                {
                    Connect();
                }

                // Parse address format: [TableName,]ColumnName[,WhereClause]
                var addressParts = ioArg.Address.Split(',');
                if (addressParts.Length < 1)
                {
                    throw new ArgumentException("Address format should be: [TableName,]ColumnName[,WhereClause]");
                }

                string tableName, columnName, whereClause;
                if (addressParts.Length == 1)
                {
                    // Only column name provided, use defaults
                    if (string.IsNullOrEmpty(DefaultTableName))
                        throw new ArgumentException("Default table name not configured");
                    tableName = DefaultTableName;
                    columnName = addressParts[0].Trim();
                    whereClause = DefaultWhereClause ?? "";
                }
                else if (addressParts.Length == 2)
                {
                    tableName = addressParts[0].Trim();
                    columnName = addressParts[1].Trim();
                    whereClause = DefaultWhereClause ?? "";
                }
                else
                {
                    tableName = addressParts[0].Trim();
                    columnName = addressParts[1].Trim();
                    whereClause = addressParts[2].Trim();
                }

                // Check if we need to refresh the cache
                var cacheKey = $"{tableName}_{columnName}_{whereClause}";
                if (_lastQueryTimes.TryGetValue(cacheKey, out var lastQueryTime))
                {
                    var timeSinceLastQuery = DateTime.Now - lastQueryTime;
                    if (timeSinceLastQuery.TotalMilliseconds < MinPeriod)
                    {
                        // Return cached value if within MinPeriod
                        returnValue.Value = _cachedQueries[cacheKey];
                        return returnValue;
                    }
                }

                // Build and execute query
                var query = $"SELECT {columnName} FROM {tableName}";
                if (!string.IsNullOrEmpty(whereClause))
                {
                    query += $" WHERE {whereClause}";
                }

                using var cmd = new OracleCommand(query, _connection);
                cmd.CommandTimeout = Timeout / 1000; // Convert to seconds

                var result = cmd.ExecuteScalar();
                if (result != null)
                {
                    // Cache the result
                    _cachedQueries[cacheKey] = result.ToString();
                    _lastQueryTimes[cacheKey] = DateTime.Now;
                    
                    returnValue.Value = result.ToString();
                }
                else
                {
                    returnValue.StatusType = VaribaleStatusTypeEnum.Bad;
                    returnValue.Message = "No data found";
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error reading from Oracle database");
                returnValue.StatusType = VaribaleStatusTypeEnum.Bad;
                returnValue.Message = ex.Message;
            }

            return returnValue;
        }

        public Task<RpcResponse> WriteAsync(string RequestId, string Method, DriverAddressIoArgModel ioArg)
        {
            // Writing to database not supported
            return Task.FromResult(new RpcResponse 
            { 
                IsSuccess = false, 
                Description = "Writing to Oracle database not supported" 
            });
        }

        public Dictionary<string, AddressDefinitionInfo> GetAddressDefinitions()
        {
            return new Dictionary<string, AddressDefinitionInfo>
            {
                {
                    "SingleColumn",
                    new AddressDefinitionInfo
                    {
                        Description = "单列查询(使用默认表)",
                        DataType = DataTypeEnum.Utf8String,
                        AddressFormat = "ColumnName - 例如: temperature 从默认表中读取temperature列"
                    }
                },
                {
                    "TableColumn",
                    new AddressDefinitionInfo
                    {
                        Description = "指定表的列查询",
                        DataType = DataTypeEnum.Utf8String,
                        AddressFormat = "TableName,ColumnName - 例如: sensors,temperature 从sensors表中读取temperature列"
                    }
                },
                {
                    "ConditionQuery",
                    new AddressDefinitionInfo
                    {
                        Description = "条件查询",
                        DataType = DataTypeEnum.Utf8String,
                        AddressFormat = "TableName,ColumnName,WhereClause - 例如: sensors,temperature,location='Zone A' AND status=1"
                    }
                },
                {
                    "NumericExample",
                    new AddressDefinitionInfo
                    {
                        Description = "数值类型示例",
                        DataType = DataTypeEnum.Float,
                        Unit = "°C",
                        AddressFormat = "device_readings,temperature,device_id=101 - 返回设备101的温度值"
                    }
                },
                {
                    "StatusExample",
                    new AddressDefinitionInfo
                    {
                        Description = "状态查询示例",
                        DataType = DataTypeEnum.Int32,
                        AddressFormat = "device_status,status,device_id=101 - 返回设备101的状态码"
                    }
                },
                {
                    "TimestampExample",
                    new AddressDefinitionInfo
                    {
                        Description = "时间戳查询示例",
                        DataType = DataTypeEnum.Utf8String,
                        AddressFormat = "device_logs,timestamp,event_type='ERROR' - 返回最新错误事件的时间戳"
                    }
                },
                {
                    "ProductionExample",
                    new AddressDefinitionInfo
                    {
                        Description = "生产数据示例",
                        DataType = DataTypeEnum.Int32,
                        Unit = "件",
                        AddressFormat = "production_stats,daily_count,line_id=1 AND date=TRUNC(SYSDATE) - 返回1号生产线今日产量"
                    }
                },
                {
                    "CalculationExample",
                    new AddressDefinitionInfo
                    {
                        Description = "聚合计算示例",
                        DataType = DataTypeEnum.Float,
                        AddressFormat = "sensor_data,AVG(value),sensor_type='TEMP' AND timestamp>SYSDATE-1/24 - 返回过去1小时的平均温度"
                    }
                }
            };
        }
    }
}
