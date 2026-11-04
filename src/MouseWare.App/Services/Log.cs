using System;
using System.Globalization;
using System.IO;

namespace MouseWare.App.Services;

/// <summary>Tiny file logger (%APPDATA%\MouseWare\mouseware.log), rotated at 1 MB.</summary>
internal static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(AppPaths.DataFolder, "mouseware.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex == null ? message : message + Environment.NewLine + ex);

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.DataFolder);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                File.AppendAllText(FilePath,
                    $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
            // Logging must never crash the app.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal static class AppPaths
{
    public static string DataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MouseWare");

    public static string ConfigFile => Path.Combine(DataFolder, "config.json");
}
