using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MouseWare.App.Native;

namespace MouseWare.App.Services;

/// <summary>Finds the program behind a window, with a small cache (this runs inside the input hook).</summary>
internal static class ProcessResolver
{
    private static readonly object Gate = new();
    private static readonly Dictionary<uint, (string Name, long Expires)> Cache = new();
    private const long CacheMs = 10_000;

    public static string? AtPoint(NativeMethods.POINT point)
    {
        var hwnd = NativeMethods.WindowFromPoint(point);
        return FromWindow(hwnd);
    }

    public static string? Foreground() => FromWindow(NativeMethods.GetForegroundWindow());

    public static string? FromWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return null;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return pid == 0 ? null : FromProcessId(pid);
    }

    public static string? FromProcessId(uint pid)
    {
        long now = Environment.TickCount64;
        lock (Gate)
        {
            if (Cache.TryGetValue(pid, out var entry) && entry.Expires > now)
                return entry.Name;
        }

        string? name = null;
        var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle != IntPtr.Zero)
        {
            try
            {
                var buffer = new StringBuilder(1024);
                uint size = (uint)buffer.Capacity;
                if (NativeMethods.QueryFullProcessImageNameW(handle, 0, buffer, ref size))
                    name = Path.GetFileName(buffer.ToString()).ToLowerInvariant();
            }
            finally
            {
                NativeMethods.CloseHandle(handle);
            }
        }

        lock (Gate)
        {
            if (Cache.Count > 512)
                Cache.Clear();
            Cache[pid] = (name ?? "", now + CacheMs);
        }
        return name;
    }
}
