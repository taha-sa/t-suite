using System;
using System.Net;
using System.Text.RegularExpressions;

namespace TSuite.Diagnostics;

/// <summary>Best-effort privacy filtering; callers should still preview reports before sharing.</summary>
public sealed class Scrubber
{
    readonly string[] identities;
    public bool MaskGuids { get; }

    public Scrubber(bool maskGuids = false) : this(Environment.UserName, Environment.MachineName, maskGuids) { }
    internal Scrubber(string userName, string machineName, bool maskGuids = false)
    {
        identities = [userName, machineName];
        MaskGuids = maskGuids;
    }

    static string Replace(string text, string pattern, string replacement) =>
        Regex.Replace(text, pattern, replacement, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public string Scrub(string? value)
    {
        string s = value ?? "";
        // Strip credentials and the entire query/fragment, including non-secret-looking parameters.
        s = Regex.Replace(s, @"https?://[^\s<>""`]+", m =>
        {
            string url = m.Value;
            int end = url.IndexOfAny(['?', '#']);
            if (end >= 0) url = url[..end] + "[query]";
            return Replace(url, @"(https?://)[^/@\s]+@", "$1[credentials]@");
        }, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        s = Replace(s, @"\b[A-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", "[email]");
        s = Replace(s, @"(\b[A-Z]:[\\/]Users[\\/])[^\\/\r\n""'<>]+", "$1[user]");
        foreach (var identity in identities)
            if (!string.IsNullOrWhiteSpace(identity))
                s = Replace(s, @"(?<![\p{L}\p{N}_])" + Regex.Escape(identity) + @"(?![\p{L}\p{N}_])", "[identity]");
        s = Regex.Replace(s, @"(?<![\w:])(?:[0-9a-f]{0,4}:){2,}[0-9a-f:.]*(?:%[\w.-]+)?", m =>
        {
            if (IPAddress.TryParse(m.Value, out _)) return "[ip]";
            string trimmed = m.Value.TrimEnd('.', ':');
            return IPAddress.TryParse(trimmed, out _) ? "[ip]" + m.Value[trimmed.Length..] : m.Value;
        },
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        s = Replace(s, @"\b(?:\d{1,3}\.){3}\d{1,3}\b", "[ip]");
        s = Replace(s, @"\bBearer\s+[^\s,;]+", "Bearer [secret]");
        s = Replace(s, @"\b((?:[\w-]*(?:token|key|auth|secret|password)[\w-]*)[""']?\s*[:=]\s*)(?:\[secret\]|""[^""\r\n]*""|'[^'\r\n]*'|Bearer\s+[^\s,;]+|[^\s,;""'&}\]]+)", "$1[secret]");
        if (MaskGuids) s = Replace(s, @"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b", "[id]");
        s = Replace(s, @"(?<![\w+/=-])(?:[0-9a-f]{32,}|[A-Za-z0-9_+/-]{40,}={0,2})(?![\w+/=-])", "[token]");
        return s;
    }
}
