using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using System;
using PluginInterface;
using Opc.Ua;
using Opc.Ua.Client;

namespace OPC.UaClient.Tests
{
    public class DeviceUaClientTests : IDisposable
    {
        private readonly Mock<ILogger<DeviceUaClient>> _loggerMock;
        private readonly DeviceUaClient _client;
        private const string TEST_DEVICE = "TestDevice";
        private const string SIMULATION_SERVER = "opc.tcp://localhost:53530/OPCUA/SimulationServer";
        private const string INVALID_SERVER = "opc.tcp://localhost:12345";
        private const string COUNTER_NODE = "ns=3;i=1001";
        private const string RANDOM_NODE = "ns=3;i=1002";
        private const string INVALID_NODE = "ns=3;i=9999";

        public DeviceUaClientTests()
        {
            _loggerMock = new Mock<ILogger<DeviceUaClient>>();
            _client = new DeviceUaClient(TEST_DEVICE, _loggerMock.Object)
            {
                Uri = SIMULATION_SERVER,
                Timeout = 60000, 
                MinPeriod = 3000,
                UseSecurity = false 
            };

            // 配置 OPC UA 客户端安全策略
            var clientConfig = new ApplicationConfiguration()
            {
                ApplicationName = "IoTGateway Test Client",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier(),
                    TrustedPeerCertificates = new CertificateTrustList(),
                    TrustedIssuerCertificates = new CertificateTrustList(),
                    RejectedCertificateStore = new CertificateTrustList(),
                    AutoAcceptUntrustedCertificates = true,
                    AddAppCertToTrustedStore = true
                },
                TransportConfigurations = new TransportConfigurationCollection(),
                TransportQuotas = new TransportQuotas { OperationTimeout = 1500 },
                ClientConfiguration = new ClientConfiguration { 
                    DefaultSessionTimeout = 6000,
                    MinSubscriptionLifetime = 5000
                }
            };

            clientConfig.Validate(ApplicationType.Client);
            _client.SetClientConfiguration(clientConfig);

            // 设置安全模式
            var endpointDescription = CoreClientUtils.SelectEndpoint(SIMULATION_SERVER, false);
            if (endpointDescription != null)
            {
                endpointDescription.SecurityMode = MessageSecurityMode.None;
                endpointDescription.SecurityPolicyUri = SecurityPolicies.None;
            }
        }

        [Fact]
        public void Connect_ToSimulationServer_ShouldSucceed()
        {
            // Act
            var result = _client.Connect();

            // Assert
            Assert.True(result, "Should successfully connect to simulation server");
            Assert.True(_client.IsConnected);
        }

        [Fact]
        public void Connect_ToInvalidServer_ShouldFail()
        {
            // Arrange
            _client.Uri = INVALID_SERVER;

            // Act
            var result = _client.Connect();

            // Assert
            Assert.False(result);
            Assert.False(_client.IsConnected);
        }

        [Fact]
        public void ReadNode_WhenNotConnected_ShouldReturnBadStatus()
        {
            // Arrange
            var nodeArg = new DriverAddressIoArgModel 
            { 
                Address = "ns=3;s=Counter1" 
            };

            // Act
            var result = _client.ReadNode(nodeArg);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(VaribaleStatusTypeEnum.Bad, result.StatusType);
            Assert.Equal("连接失败", result.Message);
        }

        [Fact]
        public void ReadNode_Counter1_ShouldReturnValue()
        {
            // Arrange
            _client.Connect();

            // Act
            var result = _client.ReadNode(new DriverAddressIoArgModel { Address = COUNTER_NODE });

            // Assert
            Assert.Equal(VaribaleStatusTypeEnum.Good, result.StatusType);
            Assert.NotNull(result.Value);
        }

        [Fact]
        public void ReadNode_RandomValue_ShouldReturnValue()
        {
            // Arrange
            _client.Connect();

            // Act
            var result = _client.ReadNode(new DriverAddressIoArgModel { Address = RANDOM_NODE });

            // Assert
            Assert.Equal(VaribaleStatusTypeEnum.Good, result.StatusType);
            Assert.NotNull(result.Value);
        }

        [Fact]
        public void ReadNode_InvalidNode_ShouldReturnBadStatus()
        {
            // Arrange
            _client.Connect();

            // Act
            var result = _client.ReadNode(new DriverAddressIoArgModel { Address = INVALID_NODE });

            // Assert
            Assert.Equal(VaribaleStatusTypeEnum.Bad, result.StatusType);
            Assert.NotNull(result.Message);
        }

        [Fact]
        public void ReadNodeAttributes_Counter1_ShouldReturnAttributes()
        {
            // Arrange
            _client.Connect();

            // Act
            var result = _client.ReadNodeAttributes(new DriverAddressIoArgModel { Address = COUNTER_NODE });

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Count > 0);
            Assert.DoesNotContain("Error", result.Keys);
            
            // 验证必要的属性存在
            Assert.Contains("NodeId", result.Keys);
            Assert.Contains("DisplayName", result.Keys);
            Assert.Contains("Value", result.Keys);
            Assert.Contains("DataType", result.Keys);
            
            // 验证属性值
            Assert.Equal(VaribaleStatusTypeEnum.Good, result["Value"].StatusType);
            Assert.NotNull(result["Value"].Value);
        }

        [Fact]
        public void ReadNodeAttributes_InvalidNode_ShouldReturnError()
        {
            // Arrange
            _client.Connect();

            // Act
            var result = _client.ReadNodeAttributes(new DriverAddressIoArgModel { Address = INVALID_NODE });

            // Assert
            Assert.NotNull(result);
            Assert.Contains("Error", result.Keys);
            Assert.Equal(VaribaleStatusTypeEnum.Bad, result["Error"].StatusType);
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}
