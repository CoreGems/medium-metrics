using System.IO;

namespace MediumMetrics.Services;

/// <summary>
/// Minimal append-only file logger. Diagnostics are best-effort and must never
/// throw — a logging failure should not break the app.
/// </summary>
public static class Log
{
    private static string? _path;
    private static readonly object Gate = new();

    public static void Init(string path) => _path = path;

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}\n{ex}");

    private static void Write(string level, string message)
    {
        if (_path is null) return;
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.AppendAllText(_path, $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Never let logging break the app.
        }
    }
}
