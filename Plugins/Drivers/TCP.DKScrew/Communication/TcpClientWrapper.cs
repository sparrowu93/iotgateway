using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace TCP.DKScrew.Communication
{
    public class TcpClientWrapper : IDisposable
    {
        private TcpClient? _client;
        private NetworkStream? _stream;
        private readonly string _host;
        private readonly int _port;
        private readonly int _timeout;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _receiveLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource? _connectionCts;

        public bool IsConnected => _client?.Connected ?? false;

        public TcpClientWrapper(string host, int port, int timeout = 5000)
        {
            _host = host;
            _port = port;
            _timeout = timeout;
        }

        public async Task ConnectAsync()
        {
            if (IsConnected)
                return;

            _connectionCts = new CancellationTokenSource();
            _client = new TcpClient();
            
            try
            {
                await _client.ConnectAsync(_host, _port);
                _stream = _client.GetStream();
                _stream.ReadTimeout = _timeout;
                _stream.WriteTimeout = _timeout;
            }
            catch (Exception)
            {
                await DisconnectAsync();
                throw;
            }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                _connectionCts?.Cancel();
                _stream?.Dispose();
                _client?.Dispose();
            }
            finally
            {
                _stream = null;
                _client = null;
                _connectionCts = null;
            }
            await Task.CompletedTask;
        }

        public async Task SendAsync(byte[] data)
        {
            if (!IsConnected || _stream == null)
                throw new InvalidOperationException("Client is not connected");

            await _sendLock.WaitAsync();
            try
            {
                await _stream.WriteAsync(data, 0, data.Length);
                await _stream.FlushAsync();
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public async Task<byte[]> ReceiveAsync(int bufferSize = 1024)
        {
            if (!IsConnected || _stream == null || _connectionCts == null)
                throw new InvalidOperationException("Client is not connected");

            await _receiveLock.WaitAsync();
            try
            {
                byte[] buffer = new byte[bufferSize];
                int totalBytesRead = 0;
                int bytesRead;

                using var cts = new CancellationTokenSource(_timeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, _connectionCts.Token);

                // Read until we find the frame footer or timeout
                do
                {
                    bytesRead = await _stream.ReadAsync(buffer, totalBytesRead, buffer.Length - totalBytesRead, linkedCts.Token);
                    if (bytesRead == 0)
                        throw new Exception("Connection closed by remote host");

                    totalBytesRead += bytesRead;

                    // Check if we have found the frame footer
                    if (totalBytesRead > 0 && buffer[totalBytesRead - 1] == Models.Protocol.FrameFooter)
                        break;

                    // If buffer is full, resize it
                    if (totalBytesRead == buffer.Length)
                    {
                        Array.Resize(ref buffer, buffer.Length * 2);
                    }
                }
                while (true);

                // Trim the buffer to actual size
                if (totalBytesRead < buffer.Length)
                {
                    Array.Resize(ref buffer, totalBytesRead);
                }

                return buffer;
            }
            finally
            {
                _receiveLock.Release();
            }
        }

        public async Task<byte[]> SendAndReceiveAsync(byte[] data, int receiveBufferSize = 1024)
        {
            await SendAsync(data);
            return await ReceiveAsync(receiveBufferSize);
        }

        public void Dispose()
        {
            _sendLock?.Dispose();
            _receiveLock?.Dispose();
            _connectionCts?.Dispose();
            _stream?.Dispose();
            _client?.Dispose();
        }
    }
}
