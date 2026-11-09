using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;

namespace TSuite.Link;

public delegate Task<JsonElement?> CommandHandler(JsonElement? data, CancellationToken ct);

/// <summary>
/// Uygulama basina named pipe sunucusu (\\.\pipe\TSuite.&lt;id&gt;). Cok istemcili, asenkron.
/// Publish() cagrisi asla bloklamaz: her istemcinin kendi kuyrugu vardir; yavas istemcide
/// "coalesce" edilebilir olaylar (varsayilan: *.progress, ram.status) anahtar basina son degere indirgenir,
/// diger olaylar sinirli kuyrukta eskiden yeniye dusurulur.
/// </summary>
public sealed class LinkServer : IAsyncDisposable
{
    readonly string _appId, _version;
    readonly string[] _events, _commands;
    readonly ConcurrentDictionary<string, CommandHandler> _handlers = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<int, Session> _sessions = new();
    readonly CancellationTokenSource _cts = new();
    readonly int _maxClients;
    int _nextSession;
    Task? _acceptLoop;

    /// <summary>Son-deger-kazanir olay adlari. "*.progress" gibi sonek desteklenir ("*" ile baslarsa).</summary>
    public List<string> CoalescedEvents { get; } = ["*.progress", "ram.status"];
    public int MaxQueuedEventsPerClient { get; set; } = 256;
    public int ClientCount => _sessions.Count;

    public LinkServer(string appId, string version, string[] events, string[] commands, int maxClients = 16)
    {
        _appId = appId; _version = version; _events = events; _commands = commands; _maxClients = maxClients;
    }

    public void OnCommand(string name, CommandHandler handler) => _handlers[name] = handler;

    public void Start()
    {
        if (_acceptLoop != null) return;
        _acceptLoop = Task.Run(AcceptLoop);
    }

    async Task AcceptLoop()
    {
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(LinkProtocol.PipeName(_appId), PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                if (_sessions.Count >= _maxClients) { pipe.Dispose(); continue; }
                var s = new Session(this, pipe, Interlocked.Increment(ref _nextSession));
                _sessions[s.Id] = s;
                _ = s.RunAsync(ct);
            }
            catch (OperationCanceledException) { pipe?.Dispose(); break; }
            catch { pipe?.Dispose(); try { await Task.Delay(200, ct).ConfigureAwait(false); } catch { break; } }
        }
    }

    /// <summary>Olay yayinlar. key: coalesce anahtari (ornegin indirme id'si). UI thread'inden guvenle cagrilabilir.</summary>
    public void Publish(string name, JsonElement? data = null, string? key = null)
    {
        if (_sessions.IsEmpty) return;
        byte[]? line = null;
        bool coalesce = IsCoalesced(name);
        foreach (var s in _sessions.Values)
        {
            // Bir istemcideki hata host uygulamaya (cagiran thread, UI dahil) asla sizmaz.
            try
            {
                if (!s.Wants(name)) continue;
                line ??= LinkJson.Serialize(new Envelope { Type = LinkProtocol.TypeEvent, Name = name, Id = key, Data = data, Ts = LinkJson.NowMs() });
                s.EnqueueEvent(line, coalesce ? name + "|" + key : null);
            }
            catch { }
        }
    }

    /// <summary>Bu olay adina abone en az bir istemci var mi? (Bos yere veri uretmemek icin.)</summary>
    public bool HasSubscriber(string eventName)
    {
        foreach (var s in _sessions.Values)
        {
            try { if (s.Wants(eventName)) return true; } catch { }
        }
        return false;
    }

    bool IsCoalesced(string name)
    {
        foreach (var p in CoalescedEvents)
        {
            if (p.Length > 0 && p[0] == '*' ? name.EndsWith(p.AsSpan(1), StringComparison.Ordinal) : name == p) return true;
        }
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        foreach (var s in _sessions.Values) s.Close();
        if (_acceptLoop != null) { try { await _acceptLoop.ConfigureAwait(false); } catch { } }
        _cts.Dispose();
    }

    internal sealed class Session
    {
        readonly LinkServer _srv;
        readonly NamedPipeServerStream _pipe;
        readonly object _lock = new();
        readonly Queue<byte[]> _replies = new();
        readonly Queue<byte[]> _events = new();
        readonly Dictionary<string, byte[]> _coalesced = new();
        readonly List<string> _coalescedOrder = new();
        readonly SemaphoreSlim _signal = new(0, 1);
        volatile string[] _prefixes = [];
        volatile bool _closed;
        public int Id { get; }

        public Session(LinkServer srv, NamedPipeServerStream pipe, int id) { _srv = srv; _pipe = pipe; Id = id; }

        public bool Wants(string name)
        {
            foreach (var p in _prefixes) if (p != null && name.StartsWith(p, StringComparison.Ordinal)) return true;
            return false;
        }

        public const int MaxPrefixes = 64, MaxPrefixLength = 256;

        /// <summary>
        /// subscribe verisini dogrular: data nesne, "prefixes" dizi olmali. Dizideki string olmayan/null/cok uzun ogeler atilir
        /// ("" = hepsi gecerlidir), en fazla MaxPrefixes oge. Gecersiz yapi -> false (mevcut abonelik degismez).
        /// </summary>
        internal static bool TryParsePrefixes(JsonElement? data, out string[] prefixes)
        {
            prefixes = [];
            if (data is not { ValueKind: JsonValueKind.Object } d) return false;
            if (!d.TryGetProperty("prefixes", out var arr) || arr.ValueKind != JsonValueKind.Array) return false;
            var list = new List<string>();
            foreach (var e in arr.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.String) continue;
                var str = e.GetString();
                if (str is null || str.Length > MaxPrefixLength) continue;
                if (list.Count >= MaxPrefixes) break;
                list.Add(str);
            }
            prefixes = list.ToArray();
            return true;
        }

        void Signal() { try { _signal.Release(); } catch (SemaphoreFullException) { } catch (ObjectDisposedException) { } }

        public void EnqueueReply(byte[] line) { lock (_lock) _replies.Enqueue(line); Signal(); }

        public void EnqueueEvent(byte[] line, string? coalesceKey)
        {
            lock (_lock)
            {
                if (coalesceKey != null)
                {
                    if (!_coalesced.ContainsKey(coalesceKey)) _coalescedOrder.Add(coalesceKey);
                    _coalesced[coalesceKey] = line;
                }
                else
                {
                    if (_events.Count >= _srv.MaxQueuedEventsPerClient) _events.Dequeue();
                    _events.Enqueue(line);
                }
            }
            Signal();
        }

        public void Close() { _closed = true; try { _pipe.Dispose(); } catch { } Signal(); }

        public async Task RunAsync(CancellationToken ct)
        {
            try
            {
                var hello = new HelloData { AppId = _srv._appId, Version = _srv._version, Events = _srv._events, Commands = _srv._commands };
                var hd = System.Text.Json.JsonSerializer.SerializeToElement(hello, LinkJsonContext.Default.HelloData);
                EnqueueReply(LinkJson.Serialize(new Envelope { Type = LinkProtocol.TypeHello, Name = "hello", Data = hd, Ts = LinkJson.NowMs() }));
                var writer = WriteLoop(ct);
                await ReadLoop(ct).ConfigureAwait(false);
                Close();
                await writer.ConfigureAwait(false);
            }
            catch { }
            finally
            {
                _closed = true;
                _srv._sessions.TryRemove(Id, out _);
                try { _pipe.Dispose(); } catch { }
            }
        }

        async Task ReadLoop(CancellationToken ct)
        {
            using var reader = new LineReader(_pipe);
            while (!ct.IsCancellationRequested && !_closed)
            {
                ReadOnlyMemory<byte>? line;
                try { line = await reader.ReadLineAsync(ct).ConfigureAwait(false); }
                catch { return; }
                if (line is null) return;
                if (line.Value.Length == 0) continue;
                Envelope? env;
                try { env = JsonSerializer.Deserialize(line.Value.Span, LinkJsonContext.Default.Envelope); }
                catch (Exception) { continue; } // bozuk satir: yoksay / ignore malformed line
                if (env == null || env.V < 1) continue;
                switch (env.Type)
                {
                    case LinkProtocol.TypeSubscribe:
                        if (TryParsePrefixes(env.Data, out var pf)) _prefixes = pf;
                        else
                        {
                            // Yalniz bu istemciye hata cevabi; baglanti ve digerleri etkilenmez.
                            var ed = LinkJson.Obj(("ok", false), ("error", "bad_subscribe"));
                            EnqueueReply(LinkJson.Serialize(new Envelope { Type = LinkProtocol.TypeReply, Id = env.Id, Name = "subscribe", Data = ed, Ts = LinkJson.NowMs() }));
                        }
                        break;
                    case LinkProtocol.TypeCmd:
                        _ = Task.Run(() => HandleCommand(env, ct), ct);
                        break;
                    // bilinmeyen tipler yoksayilir (ileri uyumluluk) / unknown types ignored
                }
            }
        }

        async Task HandleCommand(Envelope env, CancellationToken ct)
        {
            JsonElement? result = null; string? error = null;
            if (env.Name != null && _srv._handlers.TryGetValue(env.Name, out var h))
            {
                try { result = await h(env.Data, ct).ConfigureAwait(false); }
                catch (Exception ex) { error = ex.Message; }
            }
            else error = "unknown_command";
            JsonElement data = error == null
                ? LinkJson.Obj(("ok", true), ("result", result.HasValue ? (object)result.Value : null))
                : LinkJson.Obj(("ok", false), ("error", error));
            EnqueueReply(LinkJson.Serialize(new Envelope { Type = LinkProtocol.TypeReply, Id = env.Id, Name = env.Name, Data = data, Ts = LinkJson.NowMs() }));
        }

        async Task WriteLoop(CancellationToken ct)
        {
            var batch = new List<byte[]>();
            try
            {
                while (!_closed && !ct.IsCancellationRequested)
                {
                    await _signal.WaitAsync(ct).ConfigureAwait(false);
                    batch.Clear();
                    lock (_lock)
                    {
                        while (_replies.Count > 0) batch.Add(_replies.Dequeue());
                        while (_events.Count > 0) batch.Add(_events.Dequeue());
                        foreach (var k in _coalescedOrder) batch.Add(_coalesced[k]);
                        _coalesced.Clear(); _coalescedOrder.Clear();
                    }
                    foreach (var b in batch) await _pipe.WriteAsync(b, ct).ConfigureAwait(false);
                    if (batch.Count > 0) await _pipe.FlushAsync(ct).ConfigureAwait(false);
                }
            }
            catch { _closed = true; }
        }
    }
}
