using System;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using System.Collections.Generic;
using IoTGateway.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plugin;
using WalkingTec.Mvvm.Core;
using WalkingTec.Mvvm.Mvc;
using PluginInterface;

namespace IoTGateway.Areas.API
{
    /// <summary>
    /// 设备和数据查询api
    /// </summary>
    [Area("API")]
    [ActionDescription("MenuKey.ActionLog")]
    public class DeviceController : BaseController
    {
        private readonly ILogger<DeviceController> _logger;
        private readonly DeviceService _deviceService;

        public DeviceController(ILogger<DeviceController> logger, DeviceService deviceService)
        {
            _logger = logger;
            _deviceService = deviceService;
        }

        /// <summary>
        /// 获取设备列表
        /// </summary>
        /// <returns></returns>
        [Public]
        [HttpGet("Device/GetDevices")]
        public async Task<IActionResult> GetDevices()
        {
            try 
            {
                _logger.LogInformation("Getting device list...");
                
                // 先获取所有设备数量
                var totalCount = await DC.Set<Device>().CountAsync();
                _logger.LogInformation($"Total devices count: {totalCount}");
                
                // 获取所有设备（包括根节点）
                var devices = await DC.Set<Device>()
                    .Include(x => x.Driver)
                    .AsNoTracking()
                    .OrderBy(x => x.Index)
                    .ToListAsync();
                
                _logger.LogInformation($"Retrieved {devices.Count} devices");
                
                // 如果需要过滤非根节点设备，可以在这里进行
                var nonRootDevices = devices.Where(x => x.ParentId != null).ToList();
                _logger.LogInformation($"Non-root devices count: {nonRootDevices.Count}");

                foreach (var device in nonRootDevices)
                {
                    _logger.LogInformation($"Device: {device.DeviceName}, Driver: {device.Driver?.GetType().Name ?? "No Driver"}");
                }

                return Ok(new
                {
                    TotalCount = totalCount,
                    Devices = nonRootDevices,
                    Message = "Success"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting device list");
                return StatusCode(500, new { Error = ex.Message, StackTrace = ex.StackTrace });
            }
        }

        /// <summary>
        /// 控制设备变量
        /// </summary>
        /// <param name="request">控制请求</param>
        /// <returns>控制结果</returns>
        [Public]
        [HttpPost("Device/ControlDevices")]
        public async Task<IActionResult> ControlDevices([FromBody] DeviceControlRequest request)
        {
            try
            {
                if (request == null)
                    return BadRequest("Request cannot be null");

                // 获取设备变量
                var deviceVariable = await DC.Set<DeviceVariable>()
                    .Include(x => x.Device)
                        .ThenInclude(x => x.Driver)
                    .FirstOrDefaultAsync(x => x.DeviceId == request.DeviceId && x.Name == request.VariableName);

                if (deviceVariable == null)
                    return NotFound($"Device variable not found: {request.VariableName}");

                // 检查变量是否为只读
                if (deviceVariable.ProtectType == ProtectTypeEnum.ReadOnly)
                    return BadRequest("Variable is read-only");

                // 记录操作日志
                var rpcLog = new RpcLog
                {
                    DeviceId = deviceVariable.DeviceId,
                    Method = request.Method ?? deviceVariable.Method,
                    Params = JsonSerializer.Serialize(new { value = request.Value }),
                    StartTime = DateTime.Now,
                    RpcSide = RpcSide.ClientSide,
                    Description = "Device control request",
                    IsSuccess = true
                };

                try 
                {
                    // 构建RPC请求
                    var rpcRequest = new RpcRequest
                    {
                        DeviceName = deviceVariable.Device.DeviceName,
                        Method = "write",
                        Params = new Dictionary<string, object>
                        {
                            { deviceVariable.Name, request.Value }
                        },
                        RequestId = Guid.NewGuid().ToString()
                    };

                    // 获取设备线程
                    var deviceThread = _deviceService.DeviceThreads
                        .FirstOrDefault(x => x.Device.ID == deviceVariable.DeviceId);

                    if (deviceThread == null)
                        throw new Exception("Device thread not found");

                    // 执行RPC请求
                    deviceThread.MyMqttClient_OnExcRpc(this, rpcRequest);

                    rpcLog.EndTime = DateTime.Now;
                    rpcLog.Description = "Control command executed successfully";
                }
                catch (Exception ex)
                {
                    rpcLog.IsSuccess = false;
                    rpcLog.EndTime = DateTime.Now;
                    rpcLog.Description = ex.Message;
                    throw;
                }
                finally
                {
                    DC.Set<RpcLog>().Add(rpcLog);
                    await DC.SaveChangesAsync();
                }

                if (!rpcLog.IsSuccess)
                    return BadRequest(rpcLog.Description);

                return Ok(new
                {
                    DeviceName = deviceVariable.Device.DeviceName,
                    VariableName = deviceVariable.Name,
                    Value = request.Value,
                    Message = "Control command executed successfully",
                    Timestamp = rpcLog.EndTime
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing control command");
                return StatusCode(500, "Internal server error");
            }
        }
    }
}
