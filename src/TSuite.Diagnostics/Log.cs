using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TSuite.Diagnostics;

/// <summary>Thread-safe, best-effort logger. Install CrashReporter before use.</summary>
public static class Log
{
    public static void Info(string message) => CrashReporter.Current?.Logger.Write("INFO", message);
    public static void Warn(string message) => CrashReporter.Current?.Logger.Write("WARN", message);
    public static void Error(string message, Exception? exception = null) =>
        CrashReporter.Current?.Logger.Write("ERROR", message + (exception == null ? "" : "\n" + exception));
}

internal sealed class RollingLog(string directory, Scrubber scrubber, int maxBytes = 1024 * 1024)
{
    readonly object gate = new();
    internal string FilePath => Path.Combine(directory, "app.log");
    internal void Write(string level, string message)
    {
        try
        {
            // Bound individual entries too, so one exception cannot defeat rotation.
            string entry = $"{DateTime.UtcNow:O} [{level}] {scrubber.Scrub(message)}";
            int limit = Math.Max(1, (maxBytes - 80) / 4);
            if (entry.Length > limit) entry = entry[..limit] + " [truncated]";
            entry += Environment.NewLine;
            lock (gate)
            {
                Directory.CreateDirectory(directory);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length + Encoding.UTF8.GetByteCount(entry) > maxBytes)
                {
                    File.Delete(FilePath + ".2");
                    if (File.Exists(FilePath + ".1")) File.Move(FilePath + ".1", FilePath + ".2");
                    File.Move(FilePath, FilePath + ".1");
                }
                File.AppendAllText(FilePath, entry, new UTF8Encoding(false));
            }
        }
        catch (Exception) { /* Diagnostics must not crash the application. */ }
    }

    internal string[] Tail(int count = 200)
    {
        lock (gate)
        {
            try
            {
                var lines = new Queue<string>(count);
                foreach (string file in new[] { FilePath + ".2", FilePath + ".1", FilePath })
                    if (File.Exists(file)) foreach (string line in File.ReadLines(file))
                    {
                        lines.Enqueue(line);
                        if (lines.Count > count) lines.Dequeue();
                    }
                return lines.ToArray();
            }
            catch (Exception) { return []; }
        }
    }
}
