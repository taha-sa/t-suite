using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;

namespace TSuite.Link;

/// <summary>Bir T-Suite uygulamasinin pipe'ina baglanir; otomatik yeniden baglanir.</summary>
public sealed class LinkClient : IAsyncDisposable
{
    readonly string _appId;
    readonly TimeSpan _retry;
    readonly CancellationTokenSource _cts = new();
    readonly ConcurrentDictionary<string, TaskCompletionSource<Envelope>> _pending = new();
    readonly SemaphoreSlim _writeLock = new(1, 1);
    readonly TaskCompletionSource _firstConnect = new(TaskCreationOptions.RunContinuationsAsynchronously);
    volatile NamedPipeClientStream? _pipe;
    volatile string[] _prefixes = [];
    long _nextId;
    Task? _loop;

    public event Action<Envelope>? EventReceived;
    public event Action<HelloData>? Connected;
    public event Action? Disconnected;

    public HelloData? Hello { get; private set; }
    public bool IsConnected => _pipe is { IsConnected: true } && Hello != null;

    public LinkClient(string appId, TimeSpan? reconnectDelay = null)
    {
        _appId = appId; _retry = reconnectDelay ?? TimeSpan.FromSeconds(2);
    }

    /// <summary>Arka plan baglanti dongusunu baslatir. prefixes: abone olunacak olay on ekleri ("" = hepsi).</summary>
    public void Start(params string[] prefixes)
    {
        _prefixes = prefixes;
        _loop ??= Task.Run(Loop);
    }

    public async Task<bool> WaitConnectedAsync(TimeSpan timeout)
    {
        var t = await Task.WhenAny(_firstConnect.Task, Task.Delay(timeout)).ConfigureAwait(false);
        return t == _firstConnect.Task;
    }

    public async Task SubscribeAsync(params string[] prefixes)
    {
        _prefixes = prefixes;
        if (IsConnected) await SendSubscribeAsync().ConfigureAwait(false);
    }

    Task SendSubscribeAsync()
    {
        var arr = _prefixes;
        var w = new System.IO.MemoryStream();
        using (var jw = new Utf8JsonWriter(w))
        {
            jw.WriteStartObject(); jw.WriteStartArray("prefixes");
            foreach (var p in arr) jw.WriteStringValue(p);
            jw.WriteEndArray(); jw.WriteEndObject();
        }
        var data = JsonDocument.Parse(w.ToArray()).RootElement.Clone();
        return WriteAsync(new Envelope { Type = LinkProtocol.TypeSubscribe, Data = data, Ts = LinkJson.NowMs() }, _cts.Token);
    }

    async Task WriteAsync(Envelope e, CancellationToken ct)
    {
        var pipe = _pipe ?? throw new InvalidOperationException("Not connected");
        var bytes = LinkJson.Serialize(e);
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try { await pipe.WriteAsync(bytes, ct).ConfigureAwait(false); await pipe.FlushAsync(ct).ConfigureAwait(false); }
        finally { _writeLock.Release(); }
    }

    /// <summary>Komut gonderir, cevabi bekler. Zaman asiminda TimeoutException, baglanti yoksa InvalidOperationException.</summary>
    public async Task<Envelope> SendCommandAsync(string name, JsonElement? data = null, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (!IsConnected) throw new InvalidOperationException("Not connected");
        var id = Interlocked.Increment(ref _nextId).ToString();
        var tcs = new TaskCompletionSource<Envelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        try
        {
            await WriteAsync(new Envelope { Type = LinkProtocol.TypeCmd, Id = id, Name = name, Data = data, Ts = LinkJson.NowMs() }, ct).ConfigureAwait(false);
            using var tc = CancellationTokenSource.CreateLinkedTokenSource(ct);
            tc.CancelAfter(timeout ?? TimeSpan.FromSeconds(5));
            try { return await tcs.Task.WaitAsync(tc.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException($"Command '{name}' timed out"); }
        }
        finally { _pending.TryRemove(id, out _); }
    }

    async Task Loop()
    {
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            var pipe = new NamedPipeClientStream(".", LinkProtocol.PipeName(_appId), PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                using var cc = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cc.CancelAfter(1000);
                await pipe.ConnectAsync(cc.Token).ConfigureAwait(false);
                _pipe = pipe;
                await ReadLoop(pipe, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch { }
            finally
            {
                bool was = Hello != null;
                Hello = null; _pipe = null;
                try { pipe.Dispose(); } catch { }
                foreach (var kv in _pending) kv.Value.TrySetException(new IOException("Disconnected"));
                if (was) Disconnected?.Invoke();
            }
            try { await Task.Delay(_retry, ct).ConfigureAwait(false); } catch { break; }
        }
    }

    async Task ReadLoop(NamedPipeClientStream pipe, CancellationToken ct)
    {
        using var reader = new LineReader(pipe);
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) return;
            if (line.Value.Length == 0) continue;
            Envelope? env;
            try { env = JsonSerializer.Deserialize(line.Value.Span, LinkJsonContext.Default.Envelope); }
            catch (JsonException) { continue; }
            if (env == null) continue;
            switch (env.Type)
            {
                case LinkProtocol.TypeHello:
                    Hello = env.Data is { } d ? JsonSerializer.Deserialize(d, LinkJsonContext.Default.HelloData) : new HelloData();
                    _firstConnect.TrySetResult();
                    if (_prefixes.Length > 0) { try { await SendSubscribeAsync().ConfigureAwait(false); } catch { } }
                    Connected?.Invoke(Hello!);
                    break;
                case LinkProtocol.TypeReply:
                    if (env.Id != null && _pending.TryRemove(env.Id, out var tcs)) tcs.TrySetResult(env);
                    break;
                case LinkProtocol.TypeEvent:
                    try { EventReceived?.Invoke(env); } catch { }
                    break;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { _pipe?.Dispose(); } catch { }
        if (_loop != null) { try { await _loop.ConfigureAwait(false); } catch { } }
        _cts.Dispose();
    }
}
