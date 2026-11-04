using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using MouseWare.App.Native;
using MouseWare.Core;

namespace MouseWare.App.Services;

/// <summary>
/// Runs actions on its own thread so the input hook can return immediately
/// (Windows silently removes hooks that are too slow).
/// </summary>
internal sealed class ActionExecutor : IDisposable
{
    /// <summary>Tag put on every input we send, so our own hook ignores it.</summary>
    public static readonly IntPtr Marker = new(0x4D575245); // "MWRE"

    private static readonly int InputSize = Marshal.SizeOf<NativeMethods.INPUT>();
    private static readonly HashSet<string> ShellWindowClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
    };

    private readonly BlockingCollection<ActionInvocation> _queue = new();
    private readonly Thread _worker;

    /// <summary>Raised (on the worker thread) for <see cref="ActionType.ToggleEnabled"/>.</summary>
    public event Action? ToggleEnabledRequested;

    public ActionExecutor()
    {
        _worker = new Thread(Run) { IsBackground = true, Name = "MouseWare actions" };
        _worker.Start();
    }

    public void Enqueue(IReadOnlyList<ActionInvocation> invocations)
    {
        foreach (var invocation in invocations)
        {
            if (!_queue.IsAddingCompleted)
                _queue.Add(invocation);
        }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _worker.Join(TimeSpan.FromSeconds(2));
    }

    private void Run()
    {
        foreach (var invocation in _queue.GetConsumingEnumerable())
        {
            try
            {
                Execute(invocation.Action, invocation.Phase);
            }
            catch (Exception ex)
            {
                Log.Error($"Action {invocation.Action.Type} for {invocation.Trigger} failed", ex);
            }
        }
    }

    private void Execute(ActionDefinition action, InvocationPhase phase)
    {
        switch (action.Type)
        {
            case ActionType.MouseButton:
                if (action.Button is { } button)
                {
                    if (phase != InvocationPhase.Release)
                        SendMouseButton(button, down: true);
                    if (phase != InvocationPhase.Press)
                        SendMouseButton(button, down: false);
                }
                break;

            case ActionType.KeyCombo:
                if (KeyCombo.TryParse(action.Keys, out var combo))
                {
                    if (phase != InvocationPhase.Release)
                        SendKeys(combo, down: true);
                    if (phase != InvocationPhase.Press)
                        SendKeys(combo, down: false);
                }
                else
                {
                    Log.Error($"Invalid shortcut '{action.Keys}'");
                }
                break;

            case ActionType.DoubleClick:
                for (int i = 0; i < 2; i++)
                {
                    SendMouseButton(MouseButtonKind.Left, down: true);
                    SendMouseButton(MouseButtonKind.Left, down: false);
                }
                break;

            case ActionType.Scroll:
                SendScroll(action.Scroll ?? ScrollDirection.Down, Math.Clamp(action.Amount ?? 1, 1, 50));
                break;

            case ActionType.Run:
                RunProgram(action.Path, action.Arguments);
                break;

            case ActionType.OpenUrl:
                OpenUrl(action.Url);
                break;

            case ActionType.TypeText:
                SendText(action.Text ?? "");
                break;

            case ActionType.Window:
                if (action.Window is { } command)
                    WindowCommandAtCursor(command);
                break;

            case ActionType.Macro:
                RunMacro(action.Macro);
                break;

            case ActionType.ToggleEnabled:
                ToggleEnabledRequested?.Invoke();
                break;
        }
    }

    // ---- Mouse ----

    private static void SendMouseButton(MouseButtonKind button, bool down)
    {
        uint flags;
        uint data = 0;
        switch (button)
        {
            case MouseButtonKind.Left:
                flags = down ? NativeMethods.MOUSEEVENTF_LEFTDOWN : NativeMethods.MOUSEEVENTF_LEFTUP;
                break;
            case MouseButtonKind.Right:
                flags = down ? NativeMethods.MOUSEEVENTF_RIGHTDOWN : NativeMethods.MOUSEEVENTF_RIGHTUP;
                break;
            case MouseButtonKind.Middle:
                flags = down ? NativeMethods.MOUSEEVENTF_MIDDLEDOWN : NativeMethods.MOUSEEVENTF_MIDDLEUP;
                break;
            case MouseButtonKind.X1:
                flags = down ? NativeMethods.MOUSEEVENTF_XDOWN : NativeMethods.MOUSEEVENTF_XUP;
                data = NativeMethods.XBUTTON1;
                break;
            default:
                flags = down ? NativeMethods.MOUSEEVENTF_XDOWN : NativeMethods.MOUSEEVENTF_XUP;
                data = NativeMethods.XBUTTON2;
                break;
        }
        Send(MouseInput(flags, data));
    }

    private static void SendScroll(ScrollDirection direction, int amount)
    {
        bool horizontal = direction is ScrollDirection.Left or ScrollDirection.Right;
        int sign = direction is ScrollDirection.Up or ScrollDirection.Right ? 1 : -1;
        uint data = unchecked((uint)(sign * amount * NativeMethods.WHEEL_DELTA));
        Send(MouseInput(horizontal ? NativeMethods.MOUSEEVENTF_HWHEEL : NativeMethods.MOUSEEVENTF_WHEEL, data));
    }

    private static NativeMethods.INPUT MouseInput(uint flags, uint data) => new()
    {
        type = NativeMethods.INPUT_MOUSE,
        U = new NativeMethods.InputUnion
        {
            mi = new NativeMethods.MOUSEINPUT { dwFlags = flags, mouseData = data, dwExtraInfo = Marker },
        },
    };

    // ---- Keyboard ----

    private static void SendKeys(KeyCombo combo, bool down)
    {
        var inputs = new List<NativeMethods.INPUT>();
        if (down)
        {
            foreach (var modifier in combo.Modifiers)
                inputs.Add(KeyInput(modifier, keyUp: false));
            inputs.Add(KeyInput(combo.Key, keyUp: false));
        }
        else
        {
            inputs.Add(KeyInput(combo.Key, keyUp: true));
            for (int i = combo.Modifiers.Count - 1; i >= 0; i--)
                inputs.Add(KeyInput(combo.Modifiers[i], keyUp: true));
        }
        Send(inputs.ToArray());
    }

    private static NativeMethods.INPUT KeyInput(int vk, bool keyUp)
    {
        uint flags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0;
        if (KeyNames.IsExtended(vk))
            flags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;
        return new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = (ushort)vk,
                    wScan = (ushort)NativeMethods.MapVirtualKeyW((uint)vk, 0),
                    dwFlags = flags,
                    dwExtraInfo = Marker,
                },
            },
        };
    }

    private static void SendText(string text)
    {
        var inputs = new List<NativeMethods.INPUT>();
        foreach (var ch in text)
        {
            if (ch == '\r')
                continue;
            if (ch == '\n')
            {
                inputs.Add(KeyInput(0x0D, keyUp: false));
                inputs.Add(KeyInput(0x0D, keyUp: true));
                continue;
            }
            inputs.Add(UnicodeInput(ch, keyUp: false));
            inputs.Add(UnicodeInput(ch, keyUp: true));
        }
        // Send in chunks so long texts do not overflow the input queue.
        for (int i = 0; i < inputs.Count; i += 200)
        {
            Send(inputs.GetRange(i, Math.Min(200, inputs.Count - i)).ToArray());
            if (i + 200 < inputs.Count)
                Thread.Sleep(5);
        }
    }

    private static NativeMethods.INPUT UnicodeInput(char ch, bool keyUp) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        U = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = 0,
                wScan = ch,
                dwFlags = NativeMethods.KEYEVENTF_UNICODE | (keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0),
                dwExtraInfo = Marker,
            },
        },
    };

    private static void Send(params NativeMethods.INPUT[] inputs)
    {
        if (inputs.Length == 0)
            return;
        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, InputSize);
        if (sent != inputs.Length)
        {
            // Usually UIPI: the target window runs as administrator and MouseWare does not.
            Log.Error($"SendInput sent {sent}/{inputs.Length} events (error {Marshal.GetLastWin32Error()}).");
        }
    }

    // ---- Programs, links, windows ----

    private static void RunProgram(string? path, string? arguments)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        var info = new ProcessStartInfo(expanded) { UseShellExecute = true };
        if (!string.IsNullOrWhiteSpace(arguments))
            info.Arguments = Environment.ExpandEnvironmentVariables(arguments);
        var directory = Path.GetDirectoryName(expanded);
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            info.WorkingDirectory = directory;
        Process.Start(info)?.Dispose();
    }

    private static void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;
        var target = url.Trim();
        if (!target.Contains("://", StringComparison.Ordinal) && !target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            target = "https://" + target;
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri))
        {
            Log.Error($"Invalid address '{url}'");
            return;
        }
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
    }

    private static void WindowCommandAtCursor(WindowCommand command)
    {
        if (!NativeMethods.GetCursorPos(out var point))
            return;
        var hwnd = NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(point), NativeMethods.GA_ROOT);
        if (hwnd == IntPtr.Zero)
            return;

        var className = new StringBuilder(256);
        NativeMethods.GetClassNameW(hwnd, className, className.Capacity);
        if (ShellWindowClasses.Contains(className.ToString()))
            return; // never minimize/close the desktop or the taskbar

        switch (command)
        {
            case WindowCommand.Minimize:
                SysCommand(hwnd, NativeMethods.SC_MINIMIZE);
                break;
            case WindowCommand.MaximizeRestore:
                SysCommand(hwnd, NativeMethods.IsZoomed(hwnd) ? NativeMethods.SC_RESTORE : NativeMethods.SC_MAXIMIZE);
                break;
            case WindowCommand.Close:
                SysCommand(hwnd, NativeMethods.SC_CLOSE);
                break;
            case WindowCommand.ToggleTopmost:
                long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
                bool topmost = (style & NativeMethods.WS_EX_TOPMOST) != 0;
                NativeMethods.SetWindowPos(hwnd, topmost ? NativeMethods.HWND_NOTOPMOST : NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
                break;
        }
    }

    private static void SysCommand(IntPtr hwnd, int command) =>
        NativeMethods.PostMessageW(hwnd, NativeMethods.WM_SYSCOMMAND, new IntPtr(command), IntPtr.Zero);

    // ---- Macros ----

    private static void RunMacro(string? source)
    {
        var result = MacroParser.Parse(source);
        if (!result.Success)
        {
            Log.Error($"Macro has {result.Errors.Count} error(s); not run.");
            return;
        }

        var held = new List<KeyCombo>();
        try
        {
            foreach (var step in result.Steps)
            {
                switch (step)
                {
                    case KeyTapStep tap:
                        SendKeys(tap.Combo, down: true);
                        SendKeys(tap.Combo, down: false);
                        break;
                    case KeyDownStep down:
                        SendKeys(down.Combo, down: true);
                        held.Add(down.Combo);
                        break;
                    case KeyUpStep up:
                        SendKeys(up.Combo, down: false);
                        held.Remove(up.Combo);
                        break;
                    case DelayStep delay:
                        Thread.Sleep(delay.Milliseconds);
                        break;
                    case TextStep text:
                        SendText(text.Text);
                        break;
                    case ClickStep click:
                        for (int i = 0; i < click.Count; i++)
                        {
                            SendMouseButton(click.Button, down: true);
                            SendMouseButton(click.Button, down: false);
                        }
                        break;
                    case ScrollStep scroll:
                        SendScroll(scroll.Direction, scroll.Amount);
                        break;
                }
            }
        }
        finally
        {
            // Never leave keys pressed after a macro.
            for (int i = held.Count - 1; i >= 0; i--)
                SendKeys(held[i], down: false);
        }
    }
}
