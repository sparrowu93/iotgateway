﻿using MQTTnet.Client;
using Microsoft.Extensions.Logging;
using MQTTnet.Extensions.ManagedClient;
using PluginInterface;
using IoTGateway.Model;

namespace Plugin.PlatformHandler
{
    public interface IPlatformHandler
    {
        IManagedMqttClient MqttClient { get; }
        ILogger<MyMqttClient> Logger { get; }

        public event EventHandler<RpcRequest> OnExcRpc;

        Task ClientConnected();
        void ReceiveRpc(MqttApplicationMessageReceivedEventArgs e);
        Task ResponseRpcAsync(RpcResponse rpcResponse);
        Task PublishTelemetryAsync(string deviceName, Device device, Dictionary<string, List<PayLoad>> sendModel);
        Task UploadAttributeAsync(string deviceName, object obj);
        Task RequestAttributes(string deviceName, bool anySide, params string[] args);
        Task DeviceConnected(string deviceName, Device device);
        Task DeviceDisconnected(string deviceName, Device device);
        Task DeviceAdded(Device device);
        Task DeviceDeleted(Device device);
    }
}
