namespace TSuite.Update;

/// <summary>
/// UI'dan bagimsiz (headless) durum makinesi. WinForms UpdateCard bunu kullanir; WPF uygulamalari
/// (T-DeskTree) kendi XAML kartini StateChanged/ProgressChanged olaylarina baglar.
/// Olaylar arka plan thread'inden gelebilir; UI tarafi marshal etmelidir.
/// </summary>
public sealed class UpdateController
{
    readonly UpdateChecker _checker;
    CancellationTokenSource? _cts;

    public UpdateController(UpdateChecker checker) { _checker = checker; }

    public UpdateState State { get; private set; } = UpdateState.Idle;
    public UpdateInfo? Info { get; private set; }
    public double Progress { get; private set; }
    public string? Error { get; private set; }

    public event Action? StateChanged;
    public event Action<double>? ProgressChanged;
    /// <summary>Yardimci baslatildi: uygulama hemen kapanmali.</summary>
    public event Action? ExitRequested;

    void Set(UpdateState s, string? error = null) { State = s; Error = error; StateChanged?.Invoke(); }

    /// <summary>Kontrol eder; guncelleme varsa Available durumuna gecer ve true doner. Ag hatalari sessizce yutulur (Idle).</summary>
    public async Task<bool> CheckAsync(bool force = false, CancellationToken ct = default)
    {
        if (State is UpdateState.Downloading or UpdateState.Launching) return false;
        Set(UpdateState.Checking);
        try
        {
            Info = await _checker.CheckAsync(force, ct).ConfigureAwait(false);
            Set(Info != null ? UpdateState.Available : UpdateState.Idle);
            return Info != null;
        }
        catch (Exception ex)
        {
            Set(UpdateState.Idle, force ? ex.Message : null);
            return false;
        }
    }

    int _busy; // 0 = bos, 1 = indirme/kurulum suruyor (Interlocked)

    /// <summary>Elle kontrol: MinInterval atlanir. CheckAsync(force) ile ayni; API okunurlugu icin.</summary>
    public Task<bool> CheckNow(bool force = true, CancellationToken ct = default) => CheckAsync(force, ct);

    /// <summary>
    /// Kalici "bekleyen" guncellemeyi agsiz yukler (varsa Available'a gecer, true doner).
    /// Uygulama acilisinda cagrilabilir.
    /// </summary>
    public bool LoadPending()
    {
        if (State is UpdateState.Downloading or UpdateState.Launching) return false;
        var p = _checker.GetPending();
        if (p == null) return false;
        Info = p;
        Set(UpdateState.Available);
        return true;
    }

    /// <summary>Periyodik kontrol (zorlamasiz; MinInterval gecerli). Donen nesne Dispose edilince durur.</summary>
    public IDisposable PeriodicCheck(TimeSpan interval)
    {
        var cts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                using var timer = new PeriodicTimer(interval);
                while (await timer.WaitForNextTickAsync(cts.Token).ConfigureAwait(false))
                {
                    if (State is UpdateState.Idle or UpdateState.Failed)
                        await CheckAsync(false, cts.Token).ConfigureAwait(false);
                }
            }
            catch { }
        });
        return new PeriodicHandle(cts);
    }

    sealed class PeriodicHandle(CancellationTokenSource cts) : IDisposable
    {
        public void Dispose() { try { cts.Cancel(); cts.Dispose(); } catch { } }
    }

    /// <summary>Indirme/kurulum surerken true.</summary>
    public bool IsBusy => Volatile.Read(ref _busy) == 1;

    public async Task StartUpdateAsync()
    {
        if (Info == null) return;
        // Tek indirme garantisi: kontrol-sonra-ata yarisi yok.
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
        var cts = new CancellationTokenSource();
        _cts = cts;
        try
        {
            Progress = 0;
            Set(UpdateState.Downloading);
            var info = Info;
            if (info == null) { Set(UpdateState.Idle); return; }
            if (info.IsPending)
            {
                // Kalici kayit imzasiz: gercek manifesti ag uzerinden yeniden al.
                var fresh = await _checker.CheckAsync(true, cts.Token).ConfigureAwait(false);
                if (fresh == null) { Info = null; Set(UpdateState.Idle); return; }
                Info = info = fresh;
            }
            var prog = new Progress<double>(p => { Progress = p; ProgressChanged?.Invoke(p); });
            var file = await _checker.DownloadAsync(info, prog, cts.Token).ConfigureAwait(false);
            Set(UpdateState.Launching);
            UpdateChecker.LaunchUpdater(file, sha256: info.Manifest.Sha256);
            ExitRequested?.Invoke();
        }
        catch (OperationCanceledException)
        {
            // Iptal hata degildir: Idle (bekleyen bilgi Later() ile kalici yazilmistir).
            Set(UpdateState.Idle);
        }
        catch (Exception ex)
        {
            Set(UpdateState.Failed, ex.Message);
        }
        finally
        {
            if (State != UpdateState.Launching) Interlocked.Exchange(ref _busy, 0);
        }
    }

    public void Cancel() { try { _cts?.Cancel(); } catch (ObjectDisposedException) { } }

    /// <summary>"Sonra": karti kapat, bekleyen surumu kalici yaz (24 sa icinde yeniden aciliste agsiz gorunur).</summary>
    public void Later()
    {
        var info = Info;
        if (info != null) _checker.SavePending(info);
        Cancel();
        Set(UpdateState.Idle);
    }

    /// <summary>"Bu surumu atla": bu surum bir daha onerilmez.</summary>
    public void Skip()
    {
        if (Info != null) _checker.SkipVersion(Info.NewVersion);
        Info = null;
        Set(UpdateState.Idle);
    }
}
