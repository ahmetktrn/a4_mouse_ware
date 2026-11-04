using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using MouseWare.App.Native;
using MouseWare.Core;

namespace MouseWare.App.Services;

/// <summary>One observed input event, for the button test page.</summary>
internal sealed record HookEvent(string Source, string Trigger, bool IsDown, bool Injected, string? Process, bool Handled, int VirtualKey);

/// <summary>
/// Installs the low-level mouse (and, when needed, keyboard) hooks on a dedicated thread
/// with its own message loop, so a busy UI can never make the mouse lag.
/// </summary>
internal sealed class HookService : IDisposable
{
    private const uint WM_UPDATE_KEYBOARD = NativeMethods.WM_APP + 1;

    private readonly MappingEngine _engine;
    private readonly ActionExecutor _executor;
    private readonly ManualResetEventSlim _started = new();
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _mouseHook;
    private IntPtr _keyboardHook;

    // Delegates must stay referenced while the hooks exist, or the GC collects them.
    private readonly NativeMethods.LowLevelProc _mouseProc;
    private readonly NativeMethods.LowLevelProc _keyboardProc;

    private volatile bool _keyboardWanted;
    private volatile bool _monitoring;
    private volatile bool _mappingPaused;

    /// <summary>Raised on the hook thread while monitoring. Handlers must return quickly.</summary>
    public event Action<HookEvent>? InputObserved;

    public HookService(MappingEngine engine, ActionExecutor executor)
    {
        _engine = engine;
        _executor = executor;
        _mouseProc = MouseProc;
        _keyboardProc = KeyboardProc;
    }

    public bool IsRunning => _mouseHook != IntPtr.Zero;

    /// <summary>Reports events to <see cref="InputObserved"/> (button test page).</summary>
    public bool Monitoring
    {
        get => _monitoring;
        set
        {
            _monitoring = value;
            UpdateKeyboardHook();
        }
    }

    /// <summary>Lets every event through unchanged (used while testing buttons).</summary>
    public bool MappingPaused
    {
        get => _mappingPaused;
        set => _mappingPaused = value;
    }

    private volatile HashSet<int> _keyTriggers = new();

    /// <summary>Virtual-key codes used as triggers. The keyboard hook is installed only when this is not empty.</summary>
    public void SetKeyTriggers(IReadOnlyCollection<int> keys)
    {
        _keyTriggers = new HashSet<int>(keys);
        _keyboardWanted = keys.Count > 0;
        UpdateKeyboardHook();
    }

    public void Start()
    {
        if (_thread != null)
            return;
        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "MouseWare input hook",
            Priority = ThreadPriority.Highest,
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _started.Wait(TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        if (_thread == null)
            return;
        NativeMethods.PostThreadMessageW(_threadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        _executor.Enqueue(_engine.ReleaseAll());
    }

    private void UpdateKeyboardHook()
    {
        if (_threadId != 0)
            NativeMethods.PostThreadMessageW(_threadId, WM_UPDATE_KEYBOARD, IntPtr.Zero, IntPtr.Zero);
    }

    private void ThreadMain()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        var module = NativeMethods.GetModuleHandleW(null);
        _mouseHook = NativeMethods.SetWindowsHookExW(NativeMethods.WH_MOUSE_LL, _mouseProc, module, 0);
        if (_mouseHook == IntPtr.Zero)
            Log.Error($"Mouse hook could not be installed (error {Marshal.GetLastWin32Error()}).");
        else
            Log.Info("Mouse hook installed.");
        ApplyKeyboardHook(module);
        _started.Set();

        while (NativeMethods.GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_UPDATE_KEYBOARD)
                ApplyKeyboardHook(module);
        }

        if (_keyboardHook != IntPtr.Zero)
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
        if (_mouseHook != IntPtr.Zero)
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
        _keyboardHook = IntPtr.Zero;
        _mouseHook = IntPtr.Zero;
    }

    /// <summary>The keyboard hook is only installed when it is actually needed.</summary>
    private void ApplyKeyboardHook(IntPtr module)
    {
        bool wanted = _keyboardWanted || _monitoring;
        if (wanted && _keyboardHook == IntPtr.Zero)
        {
            _keyboardHook = NativeMethods.SetWindowsHookExW(NativeMethods.WH_KEYBOARD_LL, _keyboardProc, module, 0);
            if (_keyboardHook == IntPtr.Zero)
                Log.Error($"Keyboard hook could not be installed (error {Marshal.GetLastWin32Error()}).");
        }
        else if (!wanted && _keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
    }

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
            return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);

        // Movement is by far the most frequent event: let it through without any work.
        int message = (int)wParam;
        if (message == NativeMethods.WM_MOUSEMOVE)
            return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);

        try
        {
            var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            if (data.dwExtraInfo == ActionExecutor.Marker)
                return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);

            string? trigger;
            bool isDown = false;
            bool isWheel = false;
            switch (message)
            {
                case NativeMethods.WM_LBUTTONDOWN: trigger = Triggers.Left; isDown = true; break;
                case NativeMethods.WM_LBUTTONUP: trigger = Triggers.Left; break;
                case NativeMethods.WM_RBUTTONDOWN: trigger = Triggers.Right; isDown = true; break;
                case NativeMethods.WM_RBUTTONUP: trigger = Triggers.Right; break;
                case NativeMethods.WM_MBUTTONDOWN: trigger = Triggers.Middle; isDown = true; break;
                case NativeMethods.WM_MBUTTONUP: trigger = Triggers.Middle; break;
                case NativeMethods.WM_XBUTTONDOWN:
                case NativeMethods.WM_XBUTTONUP:
                    trigger = (data.mouseData >> 16) == NativeMethods.XBUTTON1 ? Triggers.X1 : Triggers.X2;
                    isDown = message == NativeMethods.WM_XBUTTONDOWN;
                    break;
                case NativeMethods.WM_MOUSEWHEEL:
                    trigger = (short)(data.mouseData >> 16) > 0 ? Triggers.WheelUp : Triggers.WheelDown;
                    isWheel = isDown = true;
                    break;
                case NativeMethods.WM_MOUSEHWHEEL:
                    trigger = (short)(data.mouseData >> 16) > 0 ? Triggers.WheelRight : Triggers.WheelLeft;
                    isWheel = isDown = true;
                    break;
                default:
                    trigger = null;
                    break;
            }

            if (trigger == null)
                return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);

            bool injected = (data.flags & NativeMethods.LLMHF_INJECTED) != 0;
            string? process = isDown ? ProcessResolver.AtPoint(data.pt) : null;

            EngineDecision decision;
            if (_mappingPaused && isDown)
                decision = EngineDecision.Pass;
            else if (isWheel)
                decision = _engine.Wheel(trigger, process);
            else if (isDown)
                decision = _engine.ButtonDown(trigger, data.pt.X, data.pt.Y, process);
            else
                decision = _engine.ButtonUp(trigger, data.pt.X, data.pt.Y); // always, so held buttons get released

            if (decision.Actions.Count > 0)
                _executor.Enqueue(decision.Actions);

            if (_monitoring)
                Observe(new HookEvent("mouse", trigger, isDown, injected, process, decision.Suppress, 0));

            return decision.Suppress ? new IntPtr(1) : NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }
        catch (Exception ex)
        {
            Log.Error("Mouse hook error", ex);
            return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }
    }

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
            return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);

        try
        {
            var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            if (data.dwExtraInfo == ActionExecutor.Marker)
                return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);

            int message = (int)wParam;
            bool isDown = message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
            bool isUp = message is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP;
            if (!isDown && !isUp)
                return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);

            int vk = (int)data.vkCode;
            var trigger = Triggers.ForKey(vk);
            bool injected = (data.flags & NativeMethods.LLKHF_INJECTED) != 0;

            EngineDecision decision = EngineDecision.Pass;
            if (isDown && !_mappingPaused && _keyTriggers.Contains(vk))
                decision = _engine.ButtonDown(trigger, 0, 0, ProcessResolver.Foreground());
            else if (isUp && _engine.HasHeld)
                decision = _engine.ButtonUp(trigger, 0, 0); // releases a key trigger that is held

            if (decision.Actions.Count > 0)
                _executor.Enqueue(decision.Actions);

            if (_monitoring)
                Observe(new HookEvent("keyboard", trigger, isDown, injected, null, decision.Suppress, vk));

            return decision.Suppress ? new IntPtr(1) : NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }
        catch (Exception ex)
        {
            Log.Error("Keyboard hook error", ex);
            return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }
    }

    private void Observe(HookEvent e)
    {
        try
        {
            InputObserved?.Invoke(e);
        }
        catch (Exception ex)
        {
            Log.Error("Input observer failed", ex);
        }
    }
}
