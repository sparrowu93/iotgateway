using System;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace WebSocket.Client.Tests.TestServer
{
    public class TestWebSocketServer : IDisposable
    {
        private readonly HttpListener _httpListener;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly string _url;
        private Task? _listenTask;
        private bool _isRunning;
        private bool _disposed;
        private readonly ILogger _logger;
        private readonly List<System.Net.WebSockets.WebSocket> _activeConnections = new();
        private readonly object _connectionsLock = new();

        public Action<string>? OnMessageReceived { get; set; }
        public Action<Exception>? OnError { get; set; }
        public Action? OnClientConnected { get; set; }
        public Action? OnClientDisconnected { get; set; }

        public TestWebSocketServer(string url, ILogger logger)
        {
            _url = url;
            _logger = logger;
            _httpListener = new HttpListener();

            _logger.LogInformation("Setting up test server with URL: {Url}", url);
            _httpListener.Prefixes.Add(url);

            _cancellationTokenSource = new CancellationTokenSource();
        }

        public void Start()
        {
            if (_disposed)
            {
                _logger.LogError("Cannot start disposed server");
                throw new ObjectDisposedException(nameof(TestWebSocketServer));
            }

            try
            {
                _logger.LogInformation("Starting WebSocket test server...");
                _httpListener.Start();
                _isRunning = true;
                _listenTask = Task.Run(ListenForClientsAsync);
                _logger.LogInformation("WebSocket test server started successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start WebSocket test server");
                throw;
            }
        }

        public void Stop()
        {
            if (_disposed)
            {
                _logger.LogDebug("Server already disposed, ignoring stop request");
                return;
            }

            if (!_isRunning)
            {
                _logger.LogDebug("Server not running, ignoring stop request");
                return;
            }

            _logger.LogInformation("Stopping WebSocket test server...");
            _isRunning = false;
            _cancellationTokenSource.Cancel();

            try
            {
                // Close all active connections
                lock (_connectionsLock)
                {
                    foreach (var connection in _activeConnections)
                    {
                        try
                        {
                            if (connection.State == WebSocketState.Open)
                            {
                                _logger.LogDebug("Closing active WebSocket connection");
                                connection.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server stopping", CancellationToken.None).Wait();
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Error closing WebSocket connection");
                        }
                    }
                    _activeConnections.Clear();
                }

                // Give the listen task a chance to complete gracefully
                if (_listenTask != null && !_listenTask.IsCompleted)
                {
                    if (!_listenTask.Wait(TimeSpan.FromSeconds(1)))
                    {
                        _logger.LogWarning("Listen task did not complete within timeout");
                    }
                }
            }
            catch (AggregateException ex)
            {
                _logger.LogDebug(ex, "Expected exception during server shutdown");
            }
            finally
            {
                if (_httpListener.IsListening)
                {
                    _logger.LogDebug("Stopping HTTP listener");
                    _httpListener.Stop();
                }
                _logger.LogInformation("WebSocket test server stopped");
            }
        }

        private async Task ListenForClientsAsync()
        {
            try
            {
                _logger.LogDebug("Starting to listen for client connections");
                while (_isRunning && !_disposed)
                {
                    try
                    {
                        var context = await _httpListener.GetContextAsync();
                        _logger.LogDebug("Received connection request");

                        if (context.Request.IsWebSocketRequest)
                        {
                            _logger.LogDebug("Processing WebSocket upgrade request");
                            var webSocketContext = await context.AcceptWebSocketAsync(null);
                            var webSocket = webSocketContext.WebSocket;

                            lock (_connectionsLock)
                            {
                                _activeConnections.Add(webSocket);
                            }

                            _logger.LogInformation("Client connection accepted");
                            OnClientConnected?.Invoke();
                            _ = HandleClientAsync(webSocket);
                        }
                        else
                        {
                            _logger.LogWarning("Received non-WebSocket request");
                            context.Response.StatusCode = 400;
                            context.Response.Close();
                        }
                    }
                    catch (Exception ex) when (ex is HttpListenerException || ex is ObjectDisposedException)
                    {
                        if (_disposed || !_isRunning)
                        {
                            _logger.LogDebug("Expected exception during shutdown: {Message}", ex.Message);
                            break;
                        }
                        _logger.LogError(ex, "Unexpected error while listening for clients");
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error in listener loop");
                OnError?.Invoke(ex);
            }
            finally
            {
                _logger.LogDebug("Listen loop terminated");
            }
        }

        private async Task HandleClientAsync(System.Net.WebSockets.WebSocket webSocket)
        {
            var buffer = new byte[4096];
            try
            {
                _logger.LogDebug("Starting to handle client messages");
                while (webSocket.State == WebSocketState.Open && !_disposed)
                {
                    var result = await webSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        _cancellationTokenSource.Token);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogInformation("Received close message from client");
                        await webSocket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Closing",
                            CancellationToken.None);
                        OnClientDisconnected?.Invoke();
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        _logger.LogDebug("Received message from client: {Message}", message);
                        OnMessageReceived?.Invoke(message);
                    }
                }
            }
            catch (OperationCanceledException) when (_disposed || !_isRunning)
            {
                _logger.LogDebug("Client handler cancelled during shutdown");
            }
            catch (WebSocketException ex) when (_disposed || !_isRunning)
            {
                _logger.LogDebug(ex, "WebSocket exception during shutdown");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling client connection");
                OnError?.Invoke(ex);
            }
            finally
            {
                _logger.LogDebug("Client handler terminated, WebSocket state: {State}", webSocket.State);
                lock (_connectionsLock)
                {
                    _activeConnections.Remove(webSocket);
                }
                OnClientDisconnected?.Invoke();
                webSocket.Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _logger.LogInformation("Disposing WebSocket test server");
            _disposed = true;
            Stop();
            _cancellationTokenSource.Dispose();
            _httpListener.Close();
        }
    }
}
