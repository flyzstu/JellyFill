using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace JellyFill.Common;

/// <summary>
/// Log severity level.
/// </summary>
public enum PluginLogLevel
{
    Debug,
    Info,
    Success,
    Warning,
    Error
}

/// <summary>
/// A single structured log entry.
/// </summary>
public class PluginLogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;

    public PluginLogLevel Level { get; set; } = PluginLogLevel.Info;

    public string Source { get; set; } = "JellyFill";

    public string Message { get; set; } = string.Empty;

    public string? Details { get; set; }
}

/// <summary>
/// In-memory circular log buffer for Jellyfin Web Dashboard.
/// </summary>
public static class PluginLogStore
{
    private const int MaxEntries = 200;
    private static readonly ConcurrentQueue<PluginLogEntry> Logs = new();

    static PluginLogStore()
    {
        Add(PluginLogLevel.Info, "JellyFill", "插件日志系统已就绪，等待媒体库触发扫描...");
    }

    public static void Add(PluginLogLevel level, string source, string message, string? details = null)
    {
        Logs.Enqueue(new PluginLogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Source = source,
            Message = message,
            Details = details
        });

        while (Logs.Count > MaxEntries)
        {
            Logs.TryDequeue(out _);
        }
    }

    public static IReadOnlyList<PluginLogEntry> GetLogs()
    {
        return Logs.ToArray().OrderByDescending(x => x.Timestamp).ToList();
    }

    public static void Clear()
    {
        while (Logs.TryDequeue(out _)) { }
        Add(PluginLogLevel.Info, "JellyFill", "日志已手动清空。");
    }
}
