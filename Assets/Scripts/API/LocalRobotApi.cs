using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;

namespace RobotArmStudio
{
    /// <summary>Loopback-only HTTP control and observation API.</summary>
    public sealed class LocalRobotApi : MonoBehaviour
    {
        private sealed class PendingRequest
        {
            public string Path;
            public string Body;
            public readonly ManualResetEvent Complete = new ManualResetEvent(false);
            public int StatusCode;
            public string ResponseBody;
            public volatile bool Cancelled;
        }

        private readonly ConcurrentQueue<PendingRequest> _inbox = new ConcurrentQueue<PendingRequest>();
        private readonly object _snapshotLock = new object();
        private HttpListener _listener;
        private Thread _serverThread;
        private RobotFleet _fleet;
        private OrbitCameraController _camera;
        private string _stateJson = "{}";
        private string _calibrationJson = "{}";
        private string _llmStatus = "未连接";
        private float _lastSnapshotTime;
        private long _heartbeatTicks;
        private const double StaleAfterSeconds = 3.0;

        public string Status { get; private set; } = "未启动";
        public int Port { get; private set; } = 8765;

        public void StartServer(RobotFleet fleet, OrbitCameraController camera, int port, string llmStatus)
        {
            _fleet = fleet;
            _camera = camera;
            Port = Mathf.Clamp(port, 1024, 65535);
            _llmStatus = llmStatus ?? "未连接";
            RefreshSnapshots();

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
                _listener.Start();
                _serverThread = new Thread(ListenLoop) { IsBackground = true, Name = "RobotArmStudio HTTP API" };
                _serverThread.Start();
                Status = $"运行中 · 127.0.0.1:{Port}";
            }
            catch (Exception e)
            {
                Status = "启动失败：" + e.Message;
                Debug.LogError("[RobotArmStudio API] " + Status);
                StopServer();
            }
        }

        public void SetLlmStatus(string status)
        {
            _llmStatus = status ?? "未连接";
        }

        private void Update()
        {
            while (_inbox.TryDequeue(out PendingRequest request))
            {
                try { ProcessRequest(request); }
                catch (Exception e)
                {
                    request.StatusCode = 500;
                    request.ResponseBody = JsonUtility.ToJson(new ApiReply { ok = false, error = e.Message });
                    Debug.LogError("[RobotArmStudio API] Request failed: " + e);
                    request.Complete.Set();
                }
            }

            if (Time.realtimeSinceStartup - _lastSnapshotTime >= 0.2f)
                RefreshSnapshots();
        }

        private void RefreshSnapshots()
        {
            if (_fleet == null) return;
            _lastSnapshotTime = Time.realtimeSinceStartup;
            string state = JsonUtility.ToJson(_fleet.CreateSnapshot(_llmStatus));
            string calibration = JsonUtility.ToJson(_fleet.CreateCalibration());
            lock (_snapshotLock)
            {
                _stateJson = state;
                _calibrationJson = calibration;
            }
            System.Threading.Interlocked.Exchange(ref _heartbeatTicks, System.Diagnostics.Stopwatch.GetTimestamp());
        }

        private void ListenLoop()
        {
            while (_listener != null && _listener.IsListening)
            {
                HttpListenerContext context;
                try { context = _listener.GetContext(); }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception) { break; }

                try { HandleHttpRequest(context); }
                catch (Exception e)
                {
                    try { WriteResponse(context.Response, 500, JsonUtility.ToJson(new ApiReply { ok = false, error = e.Message })); }
                    catch { }
                }
            }
        }

        private void HandleHttpRequest(HttpListenerContext context)
        {
            string method = context.Request.HttpMethod.ToUpperInvariant();
            string path = context.Request.Url.AbsolutePath.TrimEnd('/').ToLowerInvariant();

            if (method == "GET" && (path == "" || path == "/health"))
            {
                WriteResponse(context.Response, 200, JsonUtility.ToJson(new ApiReply { ok = true, message = "Robot Arm Studio API is running." }));
                return;
            }

            if (method == "GET" && path == "/api/state")
            {
                if (IsStale(out double staleSeconds))
                {
                    WriteResponse(context.Response, 503, JsonUtility.ToJson(new ApiReply
                    {
                        ok = false,
                        error = "simulation main loop stalled",
                        staleSeconds = (float)staleSeconds
                    }));
                    return;
                }
                string snapshot;
                lock (_snapshotLock) snapshot = _stateJson;
                WriteResponse(context.Response, 200, snapshot);
                return;
            }

            if (method == "GET" && path == "/api/calibration")
            {
                string snapshot;
                lock (_snapshotLock) snapshot = _calibrationJson;
                WriteResponse(context.Response, 200, snapshot);
                return;
            }

            if (method == "POST" && (path == "/api/command" || path == "/api/control" || path == "/api/camera"))
            {
                if (IsStale(out double staleSeconds))
                {
                    WriteResponse(context.Response, 503, JsonUtility.ToJson(new ApiReply
                    {
                        ok = false,
                        error = "simulation main loop stalled; command not queued",
                        staleSeconds = (float)staleSeconds
                    }));
                    return;
                }
                const int maxBodyBytes = 16384;
                if (context.Request.ContentLength64 > maxBodyBytes)
                {
                    WriteResponse(context.Response, 413, JsonUtility.ToJson(new ApiReply { ok = false, error = "请求体过大。" }));
                    return;
                }

                string body;
                using (var buffer = new MemoryStream())
                {
                    byte[] chunk = new byte[2048];
                    int read;
                    while ((read = context.Request.InputStream.Read(chunk, 0, chunk.Length)) > 0)
                    {
                        if (buffer.Length + read > maxBodyBytes)
                        {
                            WriteResponse(context.Response, 413, JsonUtility.ToJson(new ApiReply { ok = false, error = "请求体过大。" }));
                            return;
                        }
                        buffer.Write(chunk, 0, read);
                    }
                    body = (context.Request.ContentEncoding ?? Encoding.UTF8).GetString(buffer.ToArray());
                }

                var pending = new PendingRequest { Path = path, Body = body };
                _inbox.Enqueue(pending);
                if (!pending.Complete.WaitOne(5000))
                {
                    pending.Cancelled = true;
                    WriteResponse(context.Response, 503, JsonUtility.ToJson(new ApiReply { ok = false, error = "Unity 主线程没有及时处理命令。" }));
                    return;
                }
                WriteResponse(context.Response, pending.StatusCode, pending.ResponseBody);
                return;
            }

            WriteResponse(context.Response, 404, JsonUtility.ToJson(new ApiReply { ok = false, error = "Endpoint not found." }));
        }

        private bool IsStale(out double staleSeconds)
        {
            long heartbeat = System.Threading.Interlocked.Read(ref _heartbeatTicks);
            staleSeconds = heartbeat == 0
                ? double.MaxValue
                : (System.Diagnostics.Stopwatch.GetTimestamp() - heartbeat) / (double)System.Diagnostics.Stopwatch.Frequency;
            return staleSeconds > StaleAfterSeconds;
        }

        private void ProcessRequest(PendingRequest request)
        {
            if (request.Cancelled) return;
            if (request.Path == "/api/command")
            {
                ApiCommand command;
                try { command = JsonUtility.FromJson<ApiCommand>(request.Body); }
                catch (Exception e)
                {
                    SetReply(request, 400, new ApiReply { ok = false, error = "JSON 无效：" + e.Message });
                    return;
                }

                string reason = "机械臂控制器尚未就绪。";
                bool ok = _fleet != null && _fleet.Dispatch(command, out reason);
                SetReply(request, ok ? 200 : 400, new ApiReply { ok = ok, message = ok ? reason : null, error = ok ? null : reason });
                return;
            }

            if (request.Path == "/api/control")
            {
                ApiControlRequest control;
                try { control = JsonUtility.FromJson<ApiControlRequest>(request.Body); }
                catch (Exception e)
                {
                    SetReply(request, 400, new ApiReply { ok = false, error = "JSON 无效：" + e.Message });
                    return;
                }

                string action = control != null ? (control.action ?? "").Trim().ToLowerInvariant() : "";
                if (_fleet == null)
                {
                    SetReply(request, 503, new ApiReply { ok = false, error = "机械臂控制器尚未就绪。" });
                    return;
                }
                if (action == "pause")
                {
                    _fleet.Pause();
                    SetReply(request, 200, new ApiReply { ok = true, message = "仿真已暂停。" });
                }
                else if (action == "resume")
                {
                    _fleet.ResumeAll();
                    SetReply(request, 200, new ApiReply { ok = true, message = "仿真已恢复。" });
                }
                else if (action == "stop")
                {
                    _fleet.StopAll();
                    SetReply(request, 200, new ApiReply { ok = true, message = "急停已触发。" });
                }
                else SetReply(request, 400, new ApiReply { ok = false, error = "action 必须是 pause、resume 或 stop。" });
                return;
            }

            if (request.Path == "/api/camera")
            {
                ApiCameraRequest cameraRequest;
                try { cameraRequest = JsonUtility.FromJson<ApiCameraRequest>(request.Body); }
                catch (Exception e)
                {
                    SetReply(request, 400, new ApiReply { ok = false, error = "JSON 无效：" + e.Message });
                    return;
                }

                string reason = "相机尚未就绪。";
                bool ok = _camera != null && cameraRequest != null && _camera.SetView(cameraRequest.view, out reason);
                SetReply(request, ok ? 200 : 400, new ApiReply
                {
                    ok = ok,
                    message = ok ? "相机视角已设置。" : null,
                    error = ok ? null : reason
                });
            }
        }

        private static void SetReply(PendingRequest request, int statusCode, ApiReply reply)
        {
            request.StatusCode = statusCode;
            request.ResponseBody = JsonUtility.ToJson(reply);
            request.Complete.Set();
        }

        private static void WriteResponse(HttpListenerResponse response, int statusCode, string body)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body ?? "{}");
            response.StatusCode = statusCode;
            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            response.Headers["Cache-Control"] = "no-store";
            response.OutputStream.Write(bytes, 0, bytes.Length);
            response.Close();
        }

        private void OnDestroy()
        {
            StopServer();
        }

        private void StopServer()
        {
            try
            {
                if (_listener != null)
                {
                    if (_listener.IsListening) _listener.Stop();
                    _listener.Close();
                }
            }
            catch { }
            _listener = null;
            try
            {
                if (_serverThread != null && _serverThread.IsAlive) _serverThread.Join(250);
            }
            catch { }
            _serverThread = null;
            if (Status.StartsWith("运行中", StringComparison.Ordinal)) Status = "已停止";
        }
    }
}
