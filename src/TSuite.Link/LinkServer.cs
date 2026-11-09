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
    /// <summary>Istemci basina bekleyen yanit ust siniri. Asilinca yeni yanit yerine kucuk "reply_queue_full" hatasi gider; 2x asilirsa oturum kapanir.</summary>
    public int MaxQueuedReplies { get; set; } = 256;
    /// <summary>Istemci basina eszamanli komut ust siniri; fazlasi "busy" hata yaniti alir.</summary>
    public int MaxConcurrentCommandsPerClient { get; set; } = 8;
    /// <summary>Tek yazim zaman asimi; asilinca oturum kapanir (yavas/olu okuyucu slotu birakir).</summary>
    public TimeSpan WriteTimeout { get; set; } = TimeSpan.FromSeconds(10);
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
                s.EnqueueEvent(line, coalesce ? name + "|" + key : null, !coalesce && key != null && IsTerminal(name) ? key : null);
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

    /// <summary>Bir kimligin son olayi: bekleyen coalesce girdileri (ornegin son progress) dusurulur ki sonradan gitmesin.</summary>
    static bool IsTerminal(string name) =>
        name.EndsWith(".completed", StringComparison.Ordinal) || name.EndsWith(".failed", StringComparison.Ordinal) ||
        name.EndsWith(".canceled", StringComparison.Ordinal) || name.EndsWith(".cancelled", StringComparison.Ordinal);

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

        int _running;

        public void EnqueueReply(byte[] line, Envelope? orig = null)
        {
            bool overflow = false;
            lock (_lock)
            {
                int max = Math.Max(1, _srv.MaxQueuedReplies);
                if (_replies.Count < max) _replies.Enqueue(line);
                else if (_replies.Count < max * 2 && orig != null)
                {
                    var ed = LinkJson.Obj(("ok", false), ("error", "reply_queue_full"));
                    _replies.Enqueue(LinkJson.Serialize(new Envelope { Type = LinkProtocol.TypeReply, Id = orig.Id, Name = orig.Name, Data = ed, Ts = LinkJson.NowMs() }));
                }
                else overflow = true; // istemci okumuyor: oturumu birak
            }
            if (overflow) Close(); else Signal();
        }

        public void EnqueueEvent(byte[] line, string? coalesceKey, string? terminalKey = null)
        {
            lock (_lock)
            {
                if (terminalKey != null)
                {
                    var suffix = "|" + terminalKey;
                    for (int i = _coalescedOrder.Count - 1; i >= 0; i--)
                    {
                        var k = _coalescedOrder[i];
                        if (k.EndsWith(suffix, StringComparison.Ordinal)) { _coalesced.Remove(k); _coalescedOrder.RemoveAt(i); }
                    }
                }
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
                if (env == null || env.V < 1 || env.V > LinkProtocol.Version) continue;
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
                        if (Interlocked.Increment(ref _running) > Math.Max(1, _srv.MaxConcurrentCommandsPerClient))
                        {
                            Interlocked.Decrement(ref _running);
                            var bd = LinkJson.Obj(("ok", false), ("error", "busy"));
                            EnqueueReply(LinkJson.Serialize(new Envelope { Type = LinkProtocol.TypeReply, Id = env.Id, Name = env.Name, Data = bd, Ts = LinkJson.NowMs() }), env);
                            break;
                        }
                        var e2 = env;
                        _ = Task.Run(async () =>
                        {
                            try { await HandleCommand(e2, ct).ConfigureAwait(false); }
                            catch { }
                            finally { Interlocked.Decrement(ref _running); }
                        }, ct);
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
            EnqueueReply(LinkJson.Serialize(new Envelope { Type = LinkProtocol.TypeReply, Id = env.Id, Name = env.Name, Data = data, Ts = LinkJson.NowMs() }), env);
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
                    if (batch.Count == 0) continue;
                    // Yazim zaman asimi: okumayan istemci slotu sonsuza dek tutmasin.
                    using var wc = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    wc.CancelAfter(_srv.WriteTimeout);
                    foreach (var b in batch) await _pipe.WriteAsync(b, wc.Token).ConfigureAwait(false);
                    await _pipe.FlushAsync(wc.Token).ConfigureAwait(false);
                }
            }
            catch { _closed = true; Close(); }
        }
    }
}
