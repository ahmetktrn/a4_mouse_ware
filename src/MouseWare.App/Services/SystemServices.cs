using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading;
using System.Windows.Interop;
using Microsoft.Win32;
using MouseWare.App.Native;

namespace MouseWare.App.Services;

/// <summary>"Start with Windows" via HKCU\...\Run (the installer writes the same value).</summary>
internal static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MouseWare";

    private static string Command => $"\"{Environment.ProcessPath}\" --tray";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, Command);
        else if (key.GetValue(ValueName) != null)
            key.DeleteValue(ValueName);
    }

    /// <summary>Keeps the registered path correct if the program was moved or updated.</summary>
    public static void RefreshPath()
    {
        if (IsEnabled)
            SetEnabled(true);
    }
}

internal static class Elevation
{
    public static bool IsAdministrator
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>Starts a new elevated copy. Returns false if the user cancelled the UAC prompt.</summary>
    public static bool RestartAsAdministrator()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = "--elevated-restart",
            })?.Dispose();
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // UAC cancelled
        }
    }
}

/// <summary>
/// Global emergency shortcut (Ctrl+Alt+Shift+M) that turns mappings on/off, so a bad
/// mapping of the left/right button can always be undone from the keyboard.
/// </summary>
internal sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0x4D57;
    private const uint KeyM = 0x4D;
    private readonly HwndSource _source;
    private readonly bool _registered;

    public event Action? Pressed;

    public HotkeyService()
    {
        var parameters = new HwndSourceParameters("MouseWareHotkey")
        {
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE: invisible message-only window
            WindowStyle = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        _registered = NativeMethods.RegisterHotKey(_source.Handle, HotkeyId,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT, KeyM);
        if (!_registered)
            Log.Error("Emergency hotkey Ctrl+Alt+Shift+M could not be registered (used by another program?).");
    }

    public bool IsRegistered => _registered;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registered)
            NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        _source.Dispose();
    }
}

/// <summary>Only one MouseWare runs; starting it again brings the first window to the front.</summary>
internal sealed class SingleInstance : IDisposable
{
    public const string MutexName = "MouseWare.SingleInstance";
    private const string EventName = "MouseWare.ShowWindow";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private readonly RegisteredWaitHandle? _wait;

    public bool IsFirst { get; }

    public SingleInstance(Action onShowRequested, bool waitForPrevious)
    {
        _mutex = new Mutex(false, MutexName);
        try
        {
            // After "restart as administrator" the old copy needs a moment to exit.
            IsFirst = _mutex.WaitOne(waitForPrevious ? TimeSpan.FromSeconds(10) : TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            IsFirst = true;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        if (IsFirst)
        {
            _wait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => onShowRequested(), null, Timeout.Infinite, executeOnlyOnce: false);
        }
        else
        {
            _showEvent.Set();
        }
    }

    public void Dispose()
    {
        _wait?.Unregister(null);
        _showEvent.Dispose();
        if (IsFirst)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }
        _mutex.Dispose();
    }
}
