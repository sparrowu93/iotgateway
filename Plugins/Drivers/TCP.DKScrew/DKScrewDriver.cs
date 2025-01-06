using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using TCP.DKScrew.Models;
using TCP.DKScrew.Communication;

namespace TCP.DKScrew
{
    public class DKScrewDriver : IDisposable
    {
        private readonly TcpClientWrapper _client;
        private bool _isConnected;
        private readonly object _lock = new object();

        public event EventHandler<RunStatus>? OnStatusChanged;
        public event EventHandler<TighteningResult>? OnTighteningComplete;
        public event EventHandler<CurveData>? OnCurveDataReceived;

        public DKScrewDriver(string host, int port)
        {
            _client = new TcpClientWrapper(host, port);
        }

        public async Task ConnectAsync()
        {
            if (_isConnected)
                return;

            try
            {
                await _client.ConnectAsync();
                
                // Send connection request
                var response = await SendCommandAsync(PacketBuilder.BuildConnectionRequest());
                if (response.IsError)
                    throw new Exception($"Connection failed: Error {response.ErrorCode}");

                if (response.ResponseType != Protocol.AckResponse)
                    throw new Exception("Invalid response type for connection request");

                _isConnected = true;
            }
            catch (Exception)
            {
                await DisconnectAsync();
                throw;
            }
        }

        public async Task DisconnectAsync()
        {
            if (!_isConnected)
                return;

            try
            {
                // Send disconnect request
                await SendCommandAsync(PacketBuilder.BuildDisconnectRequest());
            }
            finally
            {
                await _client.DisconnectAsync();
                _isConnected = false;
            }
        }

        private async Task<PacketParser.ParsedPacket> SendCommandAsync(byte[] command)
        {
            CheckConnection();
            var responseData = await _client.SendAndReceiveAsync(command);
            return PacketParser.Parse(responseData);
        }

        private void CheckConnection()
        {
            if (!_isConnected)
                throw new InvalidOperationException("Not connected to the device");
        }

        // Motor Control Methods
        public async Task StartMotorAsync()
        {
            var response = await SendCommandAsync(PacketBuilder.BuildMotorControlRequest(Protocol.MotorCommand.Start));
            if (response.IsError)
                throw new Exception($"Start motor failed: Error {response.ErrorCode}");
        }

        public async Task StopMotorAsync()
        {
            var response = await SendCommandAsync(PacketBuilder.BuildMotorControlRequest(Protocol.MotorCommand.EmergencyStop));
            if (response.IsError)
                throw new Exception($"Stop motor failed: Error {response.ErrorCode}");
        }

        public async Task LoosenAsync()
        {
            var response = await SendCommandAsync(PacketBuilder.BuildMotorControlRequest(Protocol.MotorCommand.Loosen));
            if (response.IsError)
                throw new Exception($"Loosen operation failed: Error {response.ErrorCode}");
        }

        // Pset Methods
        public async Task SelectPsetAsync(int psetNumber)
        {
            if (psetNumber < 1 || psetNumber > 8)
                throw new ArgumentException("Pset number must be between 1 and 8");

            var response = await SendCommandAsync(PacketBuilder.BuildPsetSelectRequest(psetNumber));
            if (response.IsError)
                throw new Exception($"Pset selection failed: Error {response.ErrorCode}");
        }

        public async Task<PsetParameter> GetPsetDataAsync(int? psetNumber = null)
        {
            var response = await SendCommandAsync(PacketBuilder.BuildPsetDataRequest(psetNumber));
            if (response.IsError)
                throw new Exception($"Get Pset data failed: Error {response.ErrorCode}");

            // TODO: Implement Pset data parsing
            return new PsetParameter();
        }

        // Status and Result Methods
        public async Task<RunStatus> GetRunStatusAsync()
        {
            var response = await SendCommandAsync(PacketBuilder.BuildRunStatusRequest());
            if (response.IsError)
                throw new Exception($"Get run status failed: Error {response.ErrorCode}");

            var status = PacketParser.ParseRunStatus(response);
            OnStatusChanged?.Invoke(this, status);
            return status;
        }

        public async Task<TighteningResult> GetTighteningResultAsync()
        {
            var response = await SendCommandAsync(PacketBuilder.BuildTighteningResultRequest());
            if (response.IsError)
                throw new Exception($"Get tightening result failed: Error {response.ErrorCode}");

            var result = PacketParser.ParseTighteningResult(response);
            OnTighteningComplete?.Invoke(this, result);
            return result;
        }

        public async Task<CurveData> GetCurveDataAsync()
        {
            var response = await SendCommandAsync(PacketBuilder.BuildCurveDataRequest());
            if (response.IsError)
                throw new Exception($"Get curve data failed: Error {response.ErrorCode}");

            var curveData = PacketParser.ParseCurveData(response);
            OnCurveDataReceived?.Invoke(this, curveData);
            return curveData;
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}
