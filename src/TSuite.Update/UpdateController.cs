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

    public async Task StartUpdateAsync()
    {
        if (Info == null || State is UpdateState.Downloading or UpdateState.Launching) return;
        _cts = new CancellationTokenSource();
        Progress = 0;
        Set(UpdateState.Downloading);
        try
        {
            var prog = new Progress<double>(p => { Progress = p; ProgressChanged?.Invoke(p); });
            var file = await _checker.DownloadAsync(Info, prog, _cts.Token).ConfigureAwait(false);
            Set(UpdateState.Launching);
            UpdateChecker.LaunchUpdater(file);
            ExitRequested?.Invoke();
        }
        catch (Exception ex)
        {
            Set(UpdateState.Failed, ex.Message);
        }
    }

    public void Cancel() => _cts?.Cancel();

    /// <summary>"Sonra": kartı kapat; bir sonraki kontrolde yine gosterilir.</summary>
    public void Later() { Cancel(); Set(UpdateState.Idle); }

    /// <summary>"Bu surumu atla": bu surum bir daha onerilmez.</summary>
    public void Skip()
    {
        if (Info != null) _checker.SkipVersion(Info.NewVersion);
        Info = null;
        Set(UpdateState.Idle);
    }
}
