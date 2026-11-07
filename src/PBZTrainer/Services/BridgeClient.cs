using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using PBZTrainer.Models;

namespace PBZTrainer.Services
{
    public enum BridgeState
    {
        Disconnected,
        Connecting,
        Connected,
        GameMissing,
        Error
    }

    /// <summary>
    /// 与游戏内 PBZBridge.dll 的通信客户端。
    /// 协议：命名管道 ChuwuPBZ_v1_&lt;pid&gt;，每帧 = 4 字节小端长度 + UTF-8 JSON；
    /// 每个请求自动带 requestId / session / clientPid。
    /// </summary>
    public sealed partial class BridgeClient : IDisposable
    {
        public const string BuildId = "pbz-eabf89dea2775ae4eab0f75a";
        public const string GameId = "phantom-blade-zero";
        public const int Protocol = 2;
        public const string AuthorizeTicketScope = "auth_hub";

        private NamedPipeClientStream _pipe;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly string _session = Guid.NewGuid().ToString("N").ToUpperInvariant();

        public BridgeState State { get; private set; } = BridgeState.Disconnected;
        public string LastMessage { get; private set; } = "尚未连接";
        public int TargetPid { get; private set; }
        public bool IsConnected => State == BridgeState.Connected && _pipe != null && _pipe.IsConnected;

        public string Player { get; private set; } = "";
        public string World { get; private set; } = "";
        public string Scene { get; private set; } = "";
        public bool Ready { get; private set; }
        public bool CatalogReady { get; private set; }
        public bool Authorized { get; private set; }
        public string TicketId { get; private set; } = "";
        public double[] Position { get; private set; } = Array.Empty<double>();
        public Dictionary<string, JsonObject> FeatureStates { get; } = new(StringComparer.OrdinalIgnoreCase);

        public event Action Changed;

        private void Raise() => Changed?.Invoke();

        public async Task<bool> ConnectAsync(bool allowInject = true, Action<string> log = null)
        {
            Disconnect();

            var game = Injector.FindGame();
            if (game == null)
            {
                State = BridgeState.GameMissing;
                LastMessage = "请先启动游戏";
                Raise();
                return false;
            }

            TargetPid = game.Pid;
            State = BridgeState.Connecting;
            LastMessage = "正在连接";
            Raise();

            try
            {
                if (!Injector.BridgeIsLoaded(game.Pid))
                {
                    if (!allowInject) throw new InvalidOperationException("游戏接口尚未就绪");
                    string dll = Injector.ResolveBridgePath();
                    if (dll == null) throw new FileNotFoundException("缺少游戏组件 PBZBridge.dll：请把它放到本程序同目录，或设置环境变量 PBZ_BRIDGE 指向它");
                    log?.Invoke("注入组件：" + dll);
                    long handle = await Task.Run(() => Injector.Inject(game.Pid, dll));
                    log?.Invoke("组件已加载 0x" + handle.ToString("X"));
                }

                // 解除「高级功能」门禁（只改游戏进程内存，不动磁盘文件）
                try
                {
                    if (BridgeGate.Unlock(game.Pid, out string gateNote)) log?.Invoke(gateNote);
                    else log?.Invoke("门禁未解除：" + gateNote);
                }
                catch (Exception ex) { log?.Invoke("门禁处理失败：" + ex.Message); }

                // 桥接服务端刚 accept 时可能丢第一帧，握手失败就重连重试
                JsonObject hello = null;
                for (int attempt = 0; attempt < 3 && hello == null; attempt++)
                {
                    if (!await ConnectPipeAsync(game.Pid, 15000))
                        throw new TimeoutException("游戏接口尚未就绪");
                    try
                    {
                        hello = await RequestAsync(new JsonObject { ["command"] = "hello" });
                    }
                    catch
                    {
                        DisposePipe();
                        await Task.Delay(400);
                    }
                }
                if (hello == null) throw new TimeoutException("游戏接口尚未就绪");

                if (hello == null || !IsTrue(hello["ok"]))
                {
                    throw new InvalidOperationException(Str(hello?["message"]) ?? "游戏未接受连接，请重新连接");
                }
                if (Int(hello["pid"]) != game.Pid) throw new InvalidOperationException("目标进程不匹配");
                if (Int(hello["protocol"]) != Protocol) throw new InvalidOperationException("游戏接口版本不同，请重启游戏后再连接");
                string buildId = Str(hello["buildId"]);
                if (buildId != BuildId) throw new InvalidOperationException("游戏中的组件版本不同，请重启游戏后再连接");
                string gameId = Str(hello["gameId"]);
                if (gameId != GameId) throw new InvalidOperationException("游戏中的组件版本不同，请重启游戏后再连接");

                State = BridgeState.Connected;
                LastMessage = Str(hello["message"]) ?? "已连接";
                ResetAuthorization(hello);
                await RefreshAsync();
                LastMessage = Ready ? "游戏已就绪" : "等待游戏场景";
                Raise();
                return true;
            }
            catch (Exception ex)
            {
                State = BridgeState.Error;
                LastMessage = Friendly(ex);
                DisposePipe();
                Raise();
                return false;
            }
        }

        private static string Friendly(Exception ex)
        {
            if (ex is FileNotFoundException fnf) return fnf.Message;
            if (ex is TimeoutException) return "游戏接口尚未就绪";
            if (ex is IOException) return "游戏仍加载着旧组件或正被另一个窗口占用。请关闭旧修改器；更新组件后需重启游戏一次";
            if (ex is UnauthorizedAccessException) return "访问游戏进程，请保持相同运行权限";
            return string.IsNullOrWhiteSpace(ex.Message) ? "连接失败，请重试" : ex.Message;
        }

        private async Task<bool> ConnectPipeAsync(int pid, int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                var pipe = new NamedPipeClientStream(".", $"ChuwuPBZ_v1_{pid}", PipeDirection.InOut, PipeOptions.Asynchronous);
                try
                {
                    using (var cts = new CancellationTokenSource(1200))
                        await pipe.ConnectAsync(cts.Token);
                    _pipe = pipe;
                    return true;
                }
                catch
                {
                    try { pipe.Dispose(); } catch { }
                    await Task.Delay(250);
                }
            }
            return false;
        }

        public async Task<JsonObject> RefreshAsync()
        {
            var caps = await RequestAsync(new JsonObject { ["command"] = "capabilities" });
            if (caps == null) return null;
            if (caps["features"] is JsonArray arr)
            {
                FeatureStates.Clear();
                foreach (var node in arr.OfType<JsonObject>())
                {
                    string id = Str(node["id"]);
                    if (!string.IsNullOrEmpty(id)) FeatureStates[id] = node;
                }
            }
            Player = Str(caps["player"]) ?? Player;
            World = Str(caps["world"]) ?? "";
            Scene = Str(caps["scene"]) ?? "";
            Ready = IsTrue(caps["ready"]);
            CatalogReady = IsTrue(caps["catalogReady"]);
            if (caps["position"] is JsonArray pos)
            {
                Position = pos.Select(p => p == null ? 0.0 : (double)p).ToArray();
            }
            LastMessage = Str(caps["message"]) ?? LastMessage;
            Raise();
            return caps;
        }

        /// <summary>把游戏侧状态回填到界面模型。</summary>
        public void ApplyStates(IEnumerable<FeatureVm> items)
        {
            foreach (var vm in items)
            {
                if (!FeatureStates.TryGetValue(vm.Id, out var s)) continue;
                vm.IsSupported = IsTrue(s["supported"]) || !s.ContainsKey("supported");
                vm.IsSuspended = IsTrue(s["suspended"]);
                vm.Reason = Str(s["reason"]) ?? "";
                if (!vm.IsAction)
                {
                    vm.IsEnabled = IsTrue(s["enabled"]);
                }
            }
        }

        public async Task<JsonObject> ApplyFeatureAsync(FeatureVm vm, CancellationToken ct = default)
        {
            var req = new JsonObject
            {
                ["command"] = "feature",
                ["id"] = vm.Id,
                ["enabled"] = vm.IsEnabled,
                ["value"] = vm.NumericValue
            };
            return await RequestAsync(req, ct);
        }

        /// <summary>收集类功能：不断投递 collection_step 直到 pending 为 false。</summary>
        public async Task<JsonObject> RunCollectionAsync(JsonObject first, Action<int> progress, CancellationToken ct)
        {
            var result = first;
            int step = 0;
            while (result != null && IsTrue(result["pending"]))
            {
                ct.ThrowIfCancellationRequested();
                if (!IsConnected) throw new InvalidOperationException("收集操作已中止，已完成的部分请在游戏中核对");
                await Task.Delay(75, ct);
                var node = result["job"];
                result = await RequestAsync(new JsonObject
                {
                    ["command"] = "collection_step",
                    ["job"] = node?.DeepClone()
                }, ct);
                step++;
                progress?.Invoke(step);
                if (step > 4000) break;
            }
            return result;
        }

        public async Task<JsonObject> AuthorizeHubAsync(string ticket, CancellationToken ct = default)
        {
            var r = await RequestAsync(new JsonObject { ["command"] = "auth_hub", ["ticket"] = ticket }, ct);
            ResetAuthorization(r);
            return r;
        }

        public async Task<JsonObject> LogoutHubAsync() =>
            await RequestAsync(new JsonObject { ["command"] = "auth_hub_logout" });

        private void ResetAuthorization(JsonObject r)
        {
            if (r == null) return;
            bool ok = IsTrue(r["ok"]) && !IsTrue(r["busy"]) && IsTrue(r["authorized"]);
            Authorized = ok;
            TicketId = ok ? (Str(r["hubTicketId"]) ?? "") : "";
            Raise();
        }

        public async Task<JsonObject> RequestAsync(JsonObject request, CancellationToken ct = default, int timeoutMs = 0)
        {
            var pipe = _pipe;
            if (pipe == null || !pipe.IsConnected) throw new IOException("请先连接游戏");

            var payload = (JsonObject)request.DeepClone();
            payload["requestId"] = Guid.NewGuid().ToString("N").ToUpperInvariant();
            payload["session"] = _session;
            payload["clientPid"] = Environment.ProcessId;

            string command = Str(payload["command"]) ?? "";
            int budget = timeoutMs > 0 ? timeoutMs : (command == AuthorizeTicketScope ? 35000 : 4000);

            await _gate.WaitAsync(ct);
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(budget);

                byte[] body = Encoding.UTF8.GetBytes(payload.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
                byte[] frame = new byte[4 + body.Length];
                BinaryPrimitives.WriteInt32LittleEndian(frame, body.Length);
                Buffer.BlockCopy(body, 0, frame, 4, body.Length);

                await pipe.WriteAsync(frame, 0, frame.Length, cts.Token);
                await pipe.FlushAsync(cts.Token);

                byte[] len = await ReadExactAsync(pipe, 4, cts.Token);
                int n = BinaryPrimitives.ReadInt32LittleEndian(len);
                if (n <= 0 || n > 8 * 1024 * 1024) throw new IOException("游戏响应格式不正确");
                byte[] reply = await ReadExactAsync(pipe, n, cts.Token);
                string json = Encoding.UTF8.GetString(reply);
                if (string.IsNullOrWhiteSpace(json)) throw new IOException("游戏响应为空");

                var node = JsonNode.Parse(json) as JsonObject;
                if (node == null) throw new IOException("游戏响应格式不正确");
                string respSession = Str(node["session"]);
                if (!string.IsNullOrEmpty(respSession) && respSession != _session)
                    throw new IOException("响应会话不匹配");
                return node;
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("游戏响应超时，请核对游戏内是否已生效");
            }
            finally
            {
                _gate.Release();
            }
        }

        private static async Task<byte[]> ReadExactAsync(Stream s, int count, CancellationToken ct)
        {
            byte[] buf = new byte[count];
            int read = 0;
            while (read < count)
            {
                int n = await s.ReadAsync(buf.AsMemory(read, count - read), ct);
                if (n <= 0) throw new IOException("游戏响应为空");
                read += n;
            }
            return buf;
        }

        public void Disconnect()
        {
            DisposePipe();
            if (State != BridgeState.GameMissing) State = BridgeState.Disconnected;
            Ready = false;
            LastMessage = "未连接";
            Raise();
        }

        private void DisposePipe()
        {
            try { _pipe?.Dispose(); } catch { }
            _pipe = null;
        }

        public void Dispose() => Disconnect();

        private static string Str(JsonNode n)
        {
            if (n == null) return null;
            try { return n.GetValue<string>(); } catch { return n.ToJsonString(); }
        }

        private static int Int(JsonNode n)
        {
            if (n == null) return -1;
            try { return n.GetValue<int>(); } catch { return -1; }
        }

        public static bool IsTrue(JsonNode n)
        {
            if (n == null) return false;
            try { return n.GetValue<bool>(); } catch { }
            try { return n.GetValue<int>() != 0; } catch { }
            return false;
        }
    }
}
