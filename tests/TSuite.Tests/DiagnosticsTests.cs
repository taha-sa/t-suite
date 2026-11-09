using TSuite.Diagnostics;

namespace TSuite.Tests;

public sealed class DiagnosticsTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "diagnostics-tests-" + Guid.NewGuid().ToString("N"));
    ReporterSession Session(string app = "test-app") => new(app, "Test App", "1.2.3", "owner/releases", root);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Theory]
    [InlineData(@"C:\Users\Alice\file.txt on WORKSTATION", @"C:\Users\[user]\file.txt on [identity]")]
    [InlineData(@"C:\Users\Bob\secret.txt", @"C:\Users\[user]\secret.txt")]
    [InlineData(@"C:\Users\John Doe\secret.txt", @"C:\Users\[user]\secret.txt")]
    [InlineData("Alice alice@example.com 192.168.1.2", "[identity] [email] [ip]")]
    [InlineData("2001:db8:85a3::8a2e:370:7334 ::1 fe80::1%eth0", "[ip] [ip] [ip]")]
    [InlineData("::ffff:192.168.1.2", "[ip]")]
    [InlineData("Address 2001:db8::1.", "Address [ip].")]
    [InlineData("https://example.com/path?token=abc&name=Alice#secret", "https://example.com/path[query]")]
    [InlineData("https://user:pass@example.com/path", "https://[credentials]@example.com/path")]
    [InlineData("token=hello key=world auth: abc Bearer abc.def.xyz", "token=[secret] key=[secret] auth: [secret] Bearer [secret]")]
    [InlineData("Authorization: Bearer topsecret", "Authorization: [secret]")]
    [InlineData("token=\"secret with spaces\"", "token=[secret]")]
    [InlineData("{\"api_key\":\"abc123\",\"authorization\":\"secret\"}", "{\"api_key\":[secret],\"authorization\":[secret]}")]
    [InlineData("aaaabbbbccccddddeeeeffff00001111", "[token]")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuv==", "[token]")]
    [InlineData("InvalidOperationException at App.Run() line 123", "InvalidOperationException at App.Run() line 123")]
    public void Scrubs_sensitive_values(string input, string expected)
    {
        var scrubber = new Scrubber("Alice", "WORKSTATION");
        Assert.Equal(expected, scrubber.Scrub(input));
        Assert.Equal(expected, scrubber.Scrub(expected));
    }

    [Fact]
    public void Guid_filter_is_optional()
    {
        const string id = "12345678-1234-5678-abcd-123456789abc";
        Assert.Equal(id, new Scrubber().Scrub(id));
        Assert.Equal("[id]", new Scrubber(true).Scrub(id));
    }

    [Fact]
    public void Crash_survives_restart_with_metadata_inner_exceptions_and_200_log_lines()
    {
        var session = Session();
        for (int i = 0; i < 230; i++) session.Logger.Write("INFO", "line " + i);
        Exception exception;
        try { throw new InvalidOperationException("Failure token=secret", new ArgumentException("inner")); }
        catch (Exception e) { exception = e; }
        var saved = Assert.IsType<CrashReport>(session.Capture(exception));
        var restored = Assert.IsType<CrashReport>(Session().GetPendingReport());
        Assert.Equal(saved.Id, restored.Id);
        Assert.Equal("1.2.3", restored.Version);
        Assert.NotEmpty(restored.OS); Assert.Contains(".NET", restored.DotNet);
        Assert.True(restored.Utc.Kind == DateTimeKind.Utc && restored.Uptime > TimeSpan.Zero);
        Assert.Contains("DiagnosticsTests", restored.Exception.Stack);
        Assert.Single(restored.Exception.InnerExceptions);
        Assert.Equal(200, restored.LogLines.Length);
        Assert.EndsWith("line 30", restored.LogLines[0]);
        Assert.EndsWith("line 229", restored.LogLines[^1]);
        Assert.DoesNotContain("token=secret", File.ReadAllText(Path.Combine(root, "logs", "test-app", saved.Id)));
        Assert.Contains("[secret]", CrashReporter.GetReportText(restored));
    }

    [Fact]
    public void Settings_are_per_app_and_disabled_means_only_log()
    {
        var a = Session(); var b = Session("other-app");
        Assert.True(a.AutomaticallyPrepareReports);
        a.AutomaticallyPrepareReports = false;
        Assert.False(Session().AutomaticallyPrepareReports);
        Assert.True(b.AutomaticallyPrepareReports);
        Assert.Null(a.Capture(new Exception("logged only")));
        Assert.Null(a.GetPendingReport());
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "logs", "test-app"), "crash-*.json"));
        Assert.Contains("logged only", File.ReadAllText(Path.Combine(root, "logs", "test-app", "app.log")));
        a.AutomaticallyPrepareReports = true;
        Assert.NotNull(a.Capture(new Exception("prepared")));
    }

    [Fact]
    public void Completion_does_not_clear_a_newer_pending_report()
    {
        var session = Session();
        var old = session.Capture(new Exception("old"))!;
        var latest = session.Capture(new Exception("latest"))!;
        session.Mark(old);
        Assert.Equal(latest.Id, Session().GetPendingReport()!.Id);
        session.Mark(latest);
        Assert.Null(Session().GetPendingReport());
        Assert.True(File.Exists(Path.Combine(root, "logs", "test-app", latest.Id)));
    }

    [Fact]
    public void Corrupt_or_escaping_marker_is_ignored()
    {
        var session = Session(); session.Capture(new Exception("test"));
        string marker = Path.Combine(root, "logs", "test-app", "pending-crash.json");
        File.WriteAllText(marker, "garbage"); Assert.Null(session.GetPendingReport());
        File.WriteAllText(marker, "\"../crash-other.json\""); Assert.Null(session.GetPendingReport());
        Assert.Throws<ArgumentException>(() => Session("../escape"));
    }

    [Fact]
    public void Aggregate_inners_are_all_captured()
    {
        var report = Session().Capture(new AggregateException(new Exception("a"), new Exception("b")))!;
        Assert.Equal(2, report.Exception.InnerExceptions.Length);
    }

    [Fact]
    public void Issue_url_encodes_title_and_scrubbed_markdown()
    {
        var report = Session().Capture(new InvalidOperationException("wrong & token=private"))!;
        string url = CrashReporter.BuildIssueUrl(report);
        Assert.StartsWith("https://github.com/owner/releases/issues/new?title=", url);
        string decoded = Uri.UnescapeDataString(url);
        Assert.Contains("[Test App 1.2.3] InvalidOperationException: wrong & token=[secret]", decoded);
        Assert.Contains("| OS / build |", decoded);
        Assert.Contains("```text\nSystem.InvalidOperationException", decoded);
        Assert.DoesNotContain("private", decoded);
    }

    [Theory]
    [InlineData("stack line\n", 3000)]
    [InlineData("İşlem 😀\n", 3000)]
    public void Url_stays_under_limit_preserving_top_of_stack_and_closing_fence(string line, int count)
    {
        var report = Session().Capture(new Exception("failure"))!;
        report = report with { Exception = report.Exception with { Stack = "TOP_OF_STACK\n" + string.Concat(Enumerable.Repeat(line, count)) } };
        string url = CrashReporter.BuildIssueUrl(report);
        Assert.True(url.Length < 7000, $"URL length: {url.Length}");
        string decoded = Uri.UnescapeDataString(url);
        Assert.Contains("TOP_OF_STACK", decoded);
        Assert.Contains("[truncated;", decoded);
        Assert.EndsWith("\n```\n", decoded);
        Assert.DoesNotContain("�", decoded);
        Assert.Contains(string.Concat(Enumerable.Repeat(line, count)), CrashReporter.GetReportText(report));
    }

    [Fact]
    public void Huge_environment_and_title_still_fit()
    {
        var report = Session().Capture(new Exception(new string('ş', 10000)))!;
        report = report with { OS = new string('ş', 20000), ReleasesRepo = "owner/releases" };
        Assert.True(CrashReporter.BuildIssueUrl(report).Length < 7000);
        Assert.Throws<ArgumentException>(() => CrashReporter.BuildIssueUrl(report with { ReleasesRepo = "owner/repo?evil" }));
        Assert.Throws<ArgumentException>(() => CrashReporter.BuildIssueUrl(report with { ReleasesRepo = "owner/" + new string('a', 5000) }));
    }

    [Fact]
    public void Rolling_log_is_bounded_and_tail_crosses_rotations()
    {
        var logger = new RollingLog(root, new Scrubber(), 1024);
        for (int i = 0; i < 100; i++) logger.Write("INFO", $"row-{i:D3} " + new string('x', 70));
        var files = Directory.GetFiles(root);
        Assert.Equal(3, files.Length);
        Assert.All(files, f => Assert.InRange(new FileInfo(f).Length, 1, 1024));
        string tail = string.Join('\n', logger.Tail());
        Assert.Contains("row-099", tail); Assert.DoesNotContain("row-000", tail);
        Assert.Contains("row-0", File.ReadAllText(Path.Combine(root, "app.log.1")));
        logger.Write("ERROR", new string('ş', 10000));
        Assert.All(Directory.GetFiles(root), f => Assert.InRange(new FileInfo(f).Length, 1, 1024));
    }

    [Fact]
    public void Concurrent_log_writes_are_complete_lines()
    {
        var logger = new RollingLog(root, new Scrubber());
        Parallel.For(0, 100, i => logger.Write("INFO", "entry-" + i));
        Assert.Equal(100, File.ReadAllLines(logger.FilePath).Length);
        Assert.Equal(100, logger.Tail().Distinct().Count());
    }
}
