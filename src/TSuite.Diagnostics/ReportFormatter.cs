using System;
using System.Text;
using System.Text.RegularExpressions;

namespace TSuite.Diagnostics;

internal static class ReportFormatter
{
    internal static void ValidateRepo(string repo)
    {
        if (repo == null || !Regex.IsMatch(repo, @"\A[A-Za-z0-9_-]{1,39}/[A-Za-z0-9_.-]{1,100}\z") || repo.EndsWith("/.") || repo.EndsWith("/.."))
            throw new ArgumentException("Expected a GitHub owner/repository.", nameof(repo));
    }
    static string Cell(string s) => s.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ").Replace("`", "'");
    static string Header(CrashReport r) => new Scrubber().Scrub($"# Crash report\n\n| Environment | Value |\n| --- | --- |\n" +
        $"| App | {Cell(r.AppName)} |\n| Version | {Cell(r.Version)} |\n| UTC | {r.Utc:O} |\n" +
        $"| OS / build | {Cell(r.OS)} |\n| .NET | {Cell(r.DotNet)} |\n| Culture | {Cell(r.Culture)} |\n| Uptime | {r.Uptime} |\n\n");

    static string Content(CrashReport report)
    {
        var text = new StringBuilder();
        void Add(ExceptionDetails ex, int depth)
        {
            if (depth >= 32) return;
            text.AppendLine(ex.Type + ": " + ex.Message).AppendLine(ex.Stack);
            foreach (var inner in ex.InnerExceptions) { text.AppendLine("--- Inner exception ---"); Add(inner, depth + 1); }
        }
        Add(report.Exception, 0);
        text.AppendLine("--- Recent log (up to 200 lines) ---");
        foreach (var line in report.LogLines) text.AppendLine(line);
        return new Scrubber().Scrub(text.ToString()).Replace("`", "'");
    }
    internal static string Text(CrashReport report) => Header(report) + "```text\n" + Content(report) + "\n```\n";
    internal static string Url(CrashReport report)
    {
        ValidateRepo(report.ReleasesRepo);
        string type = report.Exception.Type[(report.Exception.Type.LastIndexOf('.') + 1)..];
        string title = new Scrubber().Scrub($"[{report.AppName} {report.Version}] {type}: {report.Exception.Message}")
            .Replace('\r', ' ').Replace('\n', ' ');
        title = Prefix(title, Math.Min(title.Length, 180));
        string start = $"https://github.com/{report.ReleasesRepo}/issues/new?title={Uri.EscapeDataString(title)}&body=";
        string header = Header(report);
        // Abnormal metadata must not crowd the exception out of the bounded issue URL.
        if (Uri.EscapeDataString(header).Length > 2000) header = "# Crash report\n\nEnvironment omitted (too large); see full preview.\n\n";
        string content = Content(report);
        string Build(int length) => start + Uri.EscapeDataString(header + "```text\n" + Prefix(content, length)
            + (length < content.Length ? "\n[truncated; full report available via Copy]" : "") + "\n```\n");
        int low = 0, high = content.Length;
        while (low < high)
        {
            int middle = low + (high - low + 1) / 2;
            if (Build(middle).Length < 7000) low = middle; else high = middle - 1;
        }
        return Build(low);
    }
    static string Prefix(string s, int length) => s[..(length > 0 && char.IsHighSurrogate(s[length - 1]) ? length - 1 : length)];
}
