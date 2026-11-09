using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Threading;
using TSuite.Update;

namespace TSuite.Diagnostics;

public sealed record ExceptionDetails(string Type, string Message, string? Stack, ExceptionDetails[] InnerExceptions);
public sealed record CrashReport(string Id, DateTime Utc, string AppId, string AppName, string Version,
    string ReleasesRepo, string OS, string DotNet, string Culture, TimeSpan Uptime,
    ExceptionDetails Exception, string[] LogLines);

/// <summary>Capture is local only. The host chooses when to show a pending report.</summary>
public static class CrashReporter
{
    static readonly object Gate = new();
    internal static ReporterSession? Current { get; private set; }
    static bool formsHooked;
    public static UpdateTheme Theme { get; private set; } = UpdateTheme.Dark;

    public static void Install(string appId, string appName, string version, string releasesRepo, UpdateTheme? theme = null)
    {
        lock (Gate)
        {
            if (Current != null)
            {
                if (Current.AppId != appId) throw new InvalidOperationException("A reporter is already installed for another app.");
                return;
            }
            Current = new ReporterSession(appId, appName, version, releasesRepo,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "T-Suite"));
            Theme = theme ?? UpdateTheme.Dark;
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Current.Capture(
                e.ExceptionObject as Exception ?? new Exception("Non-CLS unhandled exception"));
            TaskScheduler.UnobservedTaskException += (_, e) => Current.Capture(e.Exception);
        }
    }

    /// <summary>Call on the UI thread before Application.Run. Does not change exception handling mode.</summary>
    public static void InstallWinForms()
    {
        Require();
        lock (Gate)
        {
            if (formsHooked) return;
            System.Windows.Forms.Application.ThreadException += (_, e) => Current?.Capture(e.Exception);
            formsHooked = true;
        }
    }

    /// <summary>Does not set Handled; fatal dispatcher exceptions retain WPF's normal behavior.</summary>
    public static IDisposable InstallWpf(Dispatcher dispatcher)
    {
        Require();
        ArgumentNullException.ThrowIfNull(dispatcher);
        DispatcherUnhandledExceptionEventHandler handler = (_, e) => Current?.Capture(e.Exception);
        dispatcher.UnhandledException += handler;
        return new Subscription(() => dispatcher.UnhandledException -= handler);
    }

    sealed class Subscription(Action detach) : IDisposable
    {
        public void Dispose() => System.Threading.Interlocked.Exchange(ref detach, null!)?.Invoke();
    }

    static ReporterSession Require() => Current ?? throw new InvalidOperationException("Call CrashReporter.Install first.");
    public static bool AutomaticallyPrepareReports
    {
        get => Require().AutomaticallyPrepareReports;
        set => Require().AutomaticallyPrepareReports = value;
    }
    public static CrashReport? GetPendingReport() => Require().GetPendingReport();
    public static void MarkReported(CrashReport report) => Require().Mark(report);
    public static void MarkDismissed(CrashReport report) => Require().Mark(report);
    public static string GetReportText(CrashReport report) => ReportFormatter.Text(report);
    public static string BuildIssueUrl(CrashReport report) => ReportFormatter.Url(report);
}

internal sealed class ReporterSession
{
    static readonly object SettingsGate = new();
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    readonly object gate = new();
    readonly string directory, settingsPath, appName, version, repo;
    readonly Scrubber scrubber = new();
    internal string AppId { get; }
    internal RollingLog Logger { get; }
    string Marker => Path.Combine(directory, "pending-crash.json");

    internal ReporterSession(string appId, string appName, string version, string releasesRepo, string root)
    {
        if (!Regex.IsMatch(appId, @"\A[a-zA-Z0-9][a-zA-Z0-9._-]{0,99}\z") || appId.EndsWith('.'))
            throw new ArgumentException("Use a simple app ID without path separators.", nameof(appId));
        ReportFormatter.ValidateRepo(releasesRepo);
        AppId = appId; this.appName = appName; this.version = version; repo = releasesRepo;
        directory = Path.Combine(root, "logs", appId);
        settingsPath = Path.Combine(root, "diagnostics.json");
        Logger = new RollingLog(directory, scrubber);
    }

    Dictionary<string, bool> ReadSettings()
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(settingsPath)) ?? new(); }
        catch (Exception) { return new(); }
    }
    internal bool AutomaticallyPrepareReports
    {
        get { lock (SettingsGate) return !ReadSettings().TryGetValue(AppId, out bool enabled) || enabled; }
        set
        {
            lock (SettingsGate)
            {
                var settings = ReadSettings(); settings[AppId] = value;
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
                AtomicWrite(settingsPath, JsonSerializer.Serialize(settings, Json));
            }
        }
    }

    internal CrashReport? Capture(Exception exception)
    {
        lock (gate)
        {
            try
            {
                if (!AutomaticallyPrepareReports) { Logger.Write("ERROR", exception.ToString()); return null; }
                string id = "crash-" + DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfffffff'Z'", CultureInfo.InvariantCulture)
                    + "-" + Guid.NewGuid().ToString("N") + ".json";
                using var process = Process.GetCurrentProcess();
                var report = new CrashReport(id, DateTime.UtcNow, AppId, scrubber.Scrub(appName), scrubber.Scrub(version),
                    repo, scrubber.Scrub(RuntimeInformation.OSDescription + " (" + Environment.OSVersion.Version + ")"),
                    RuntimeInformation.FrameworkDescription, CultureInfo.CurrentCulture.Name,
                    DateTime.UtcNow - process.StartTime.ToUniversalTime(), Details(exception, new HashSet<Exception>(), 0), Logger.Tail());
                Directory.CreateDirectory(directory);
                AtomicWrite(Path.Combine(directory, id), JsonSerializer.Serialize(report, Json));
                AtomicWrite(Marker, JsonSerializer.Serialize(id));
                return report;
            }
            catch (Exception) { return null; } // Includes shutdown/disk/access failures; never recurse into reporting.
        }
    }

    ExceptionDetails Details(Exception ex, HashSet<Exception> seen, int depth)
    {
        if (depth >= 32 || !seen.Add(ex)) return new("[truncated]", "Inner exception limit reached", null, []);
        var inner = ex is AggregateException aggregate ? aggregate.InnerExceptions :
            ex.InnerException is { } one ? new[] { one }.AsEnumerable() : Enumerable.Empty<Exception>();
        return new(scrubber.Scrub(ex.GetType().FullName), scrubber.Scrub(ex.Message), scrubber.Scrub(ex.StackTrace),
            inner.Take(32).Select(e => Details(e, seen, depth + 1)).ToArray());
    }

    string? PendingId()
    {
        if (!File.Exists(Marker)) return null;
        string? id = JsonSerializer.Deserialize<string>(File.ReadAllText(Marker));
        return id != null && Path.GetFileName(id) == id && id.StartsWith("crash-", StringComparison.Ordinal)
            && id.EndsWith(".json", StringComparison.Ordinal) ? id : null;
    }
    internal CrashReport? GetPendingReport()
    {
        lock (gate)
        {
            try
            {
                if (!AutomaticallyPrepareReports) return null;
                var id = PendingId();
                if (id == null) return null;
                var report = JsonSerializer.Deserialize<CrashReport>(File.ReadAllText(Path.Combine(directory, id)));
                return report?.Id == id && report.AppId == AppId ? report : null;
            }
            catch (Exception) { return null; }
        }
    }
    internal void Mark(CrashReport report)
    {
        lock (gate)
        {
            try { if (report.AppId == AppId && PendingId() == report.Id) File.Delete(Marker); }
            catch (Exception) { }
        }
    }
    static void AtomicWrite(string path, string value)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, value); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
