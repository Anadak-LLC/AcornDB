using System;
using AcornDB.Logging;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AcornDB.Metrics;

namespace AcornDB.Metrics
{
    /// <summary>
    /// Lightweight HTTP server for exposing Prometheus/OpenTelemetry metrics.
    /// Does not require ASP.NET Core - uses HttpListener for standalone operation.
    ///
    /// Usage:
    /// <code>
    /// // Start metrics server on port 9090
    /// var server = new MetricsServer(port: 9090);
    /// server.Start();
    ///
    /// // Later, stop the server
    /// server.Stop();
    /// </code>
    ///
    /// Endpoints:
    /// - GET /metrics - Prometheus text format (default)
    /// - GET /metrics?format=json - JSON format (OpenTelemetry compatible)
    /// - GET /health - Health check endpoint
    /// </summary>
    public class MetricsServer : IDisposable
    {
        private readonly int _port;
        private readonly string _path;
        private HttpListener? _listener;
        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _listenerTask;
        private bool _disposed;

        /// <summary>
        /// Create a new metrics server
        /// </summary>
        /// <param name="port">Port to listen on (default: 9090)</param>
        /// <param name="path">Metrics endpoint path (default: "/metrics")</param>
        public MetricsServer(int port = 9090, string path = "/metrics")
        {
            _port = port;
            _path = path.StartsWith("/") ? path : $"/{path}";
        }

        /// <summary>
        /// Start the metrics server
        /// </summary>
        public void Start()
        {
            if (_listener != null)
            {
                AcornLog.Info($"⚠️ Metrics server already running on port {_port}");
                return;
            }

            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://+:{_port}/");

            try
            {
                _listener.Start();
                _cancellationTokenSource = new CancellationTokenSource();
                _listenerTask = Task.Run(() => ListenAsync(_cancellationTokenSource.Token));

                AcornLog.Info($"📊 Metrics server started on http://localhost:{_port}{_path}");
                AcornLog.Info($"   Prometheus: http://localhost:{_port}{_path}");
                AcornLog.Info($"   JSON:       http://localhost:{_port}{_path}?format=json");
                AcornLog.Info($"   Health:     http://localhost:{_port}/health");
            }
            catch (Exception ex)
            {
                AcornLog.Info($"❌ Failed to start metrics server: {ex.Message}");
                _listener?.Stop();
                _listener = null;
            }
        }

        /// <summary>
        /// Stop the metrics server
        /// </summary>
        public void Stop()
        {
            if (_listener == null) return;

            AcornLog.Info($"🛑 Stopping metrics server on port {_port}");

            _cancellationTokenSource?.Cancel();
            _listener.Stop();
            _listenerTask?.Wait(TimeSpan.FromSeconds(5));

            _listener = null;
            _listenerTask = null;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }

        private async Task ListenAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _listener != null && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequestAsync(context), cancellationToken);
                }
                catch (HttpListenerException)
                {
                    // Listener was stopped
                    break;
                }
                catch (Exception ex)
                {
                    AcornLog.Info($"⚠️ Error in metrics server: {ex.Message}");
                }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;

                // Health check endpoint
                if (request.Url?.AbsolutePath == "/health")
                {
                    response.StatusCode = 200;
                    response.ContentType = "application/json";
                    var health = "{\"status\":\"healthy\",\"timestamp\":\"" + DateTime.UtcNow.ToString("o") + "\"}";
                    var buffer = Encoding.UTF8.GetBytes(health);
                    response.ContentLength64 = buffer.Length;
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    response.OutputStream.Close();
                    return;
                }

                // Metrics endpoint
                if (request.Url?.AbsolutePath == _path)
                {
                    var format = request.QueryString["format"]?.ToLowerInvariant() ?? "prometheus";

                    string content;
                    string contentType;

                    if (format == "json")
                    {
                        content = MetricsCollector.Instance.ExportJson();
                        contentType = "application/json";
                    }
                    else
                    {
                        content = MetricsCollector.Instance.ExportPrometheus();
                        contentType = "text/plain; version=0.0.4; charset=utf-8";
                    }

                    response.StatusCode = 200;
                    response.ContentType = contentType;
                    var buffer = Encoding.UTF8.GetBytes(content);
                    response.ContentLength64 = buffer.Length;
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    response.OutputStream.Close();
                    return;
                }

                // 404 for other paths
                response.StatusCode = 404;
                response.ContentType = "text/plain";
                var notFound = Encoding.UTF8.GetBytes("Not Found");
                response.ContentLength64 = notFound.Length;
                await response.OutputStream.WriteAsync(notFound, 0, notFound.Length);
                response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                AcornLog.Info($"⚠️ Error handling request: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Stop();
        }
    }

    /// <summary>
    /// Configuration helper for metrics system
    /// </summary>
    public static class MetricsConfiguration
    {
        /// <summary>
        /// Configure metrics labels (environment, region, instance, etc.)
        /// </summary>
        public static void ConfigureLabels(
            string? environment = null,
            string? region = null,
            string? instance = null)
        {
            if (environment != null)
                MetricsCollector.Instance.AddLabel("environment", environment);

            if (region != null)
                MetricsCollector.Instance.AddLabel("region", region);

            if (instance != null)
                MetricsCollector.Instance.AddLabel("instance", instance);
        }
    }
}
