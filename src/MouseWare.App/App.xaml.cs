using System;
using System.Linq;
using System.Windows;
using MouseWare.App.Localization;
using MouseWare.App.Services;
using MouseWare.App.UI;
using MouseWare.Core;

namespace MouseWare.App;

public partial class App : Application
{
    private SingleInstance? _instance;
    private ActionExecutor? _executor;
    private HotkeyService? _hotkeys;
    private TrayIcon? _tray;
    private MainWindow? _main;
    private bool _backgroundHintShown;
    private bool _exiting;

    internal static new App Current => (App)Application.Current;

    internal ConfigService Config { get; } = new();
    internal MappingEngine Engine { get; private set; } = null!;
    internal HookService Hook { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled UI exception", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Unhandled exception", args.ExceptionObject as Exception);

        bool elevatedRestart = e.Args.Contains("--elevated-restart");
        _instance = new SingleInstance(() => Dispatcher.InvokeAsync(() => ShowMain()), waitForPrevious: elevatedRestart);
        if (!_instance.IsFirst)
        {
            _instance.Dispose();
            Shutdown();
            return;
        }

        Config.Load();
        Loc.Instance.Language = Config.Current.Language;

        _executor = new ActionExecutor();
        _executor.ToggleEnabledRequested += () => Dispatcher.InvokeAsync(ToggleEnabled);

        Engine = new MappingEngine(Config.Snapshot());
        Hook = new HookService(Engine, _executor);
        Hook.SetKeyTriggers(Engine.Config.KeyTriggers());
        Config.Changed += ApplyConfig;
        Hook.Start();

        _hotkeys = new HotkeyService();
        _hotkeys.Pressed += ToggleEnabled;

        _tray = new TrayIcon();
        _tray.SetEnabled(Config.Current.Enabled);
        _tray.OpenRequested += () => ShowMain();
        _tray.TestRequested += () => ShowMain(UI.MainWindow.TestTabIndex);
        _tray.ToggleRequested += ToggleEnabled;
        _tray.ExitRequested += ExitApp;

        try
        {
            StartupManager.RefreshPath();
        }
        catch (Exception ex)
        {
            Log.Error("Could not refresh start-up entry", ex);
        }

        Log.Info($"MouseWare started (admin: {Elevation.IsAdministrator}).");

        bool startHidden = e.Args.Contains("--tray");
        if (!startHidden)
            ShowMain();
        else
            _backgroundHintShown = true; // started with Windows: stay quiet

        if (!Hook.IsRunning)
            MessageBox.Show(Loc.T("Hook_Failed"), "MouseWare", MessageBoxButton.OK, MessageBoxImage.Error);
        if (Config.LoadError != null)
            MessageBox.Show(Loc.F("Settings_ConfigBroken", Config.LoadError), "MouseWare", MessageBoxButton.OK, MessageBoxImage.Warning);
        if (!_hotkeys.IsRegistered)
            _tray.ShowBalloon("MouseWare", Loc.T("Hotkey_Failed"));
    }

    private void ApplyConfig()
    {
        var snapshot = Config.Snapshot();
        Engine.UpdateConfig(snapshot);
        Hook.SetKeyTriggers(snapshot.KeyTriggers());
        Loc.Instance.Language = snapshot.Language;
        _tray?.SetEnabled(snapshot.Enabled);
        _main?.RefreshEnabled();
    }

    /// <summary>Turns all mappings on/off (emergency hotkey, tray menu, header switch).</summary>
    internal void ToggleEnabled() => SetEnabled(!Config.Current.Enabled, notify: true);

    internal void SetEnabled(bool enabled, bool notify)
    {
        if (Config.Current.Enabled == enabled)
            return;
        Config.Current.Enabled = enabled;
        Config.Save();
        if (notify)
            _tray?.ShowBalloon("MouseWare", Loc.T(enabled ? "Toggle_On" : "Toggle_Off"));
    }

    internal void ShowMain(int? tabIndex = null)
    {
        if (_exiting)
            return;
        if (_main == null)
        {
            _main = new MainWindow();
            _main.Closed += (_, _) => _main = null;
        }
        _main.Show();
        if (_main.WindowState == WindowState.Minimized)
            _main.WindowState = WindowState.Normal;
        _main.Activate();
        if (tabIndex != null)
            _main.SelectTab(tabIndex.Value);
    }

    /// <summary>Called when the main window is closed with X: the app keeps running in the tray.</summary>
    internal void OnMainHidden()
    {
        if (_backgroundHintShown)
            return;
        _backgroundHintShown = true;
        _tray?.ShowBalloon(Loc.T("Tray_RunningTitle"), Loc.T("Tray_RunningText"));
    }

    internal void RestartAsAdministrator()
    {
        if (Elevation.RestartAsAdministrator())
            ExitApp();
    }

    internal void ExitApp()
    {
        if (_exiting)
            return;
        _exiting = true;
        Log.Info("MouseWare exiting.");
        _main?.CloseForExit();
        Hook?.Dispose();
        _executor?.Dispose();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _instance?.Dispose();
        Shutdown();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        ExitApp();
        base.OnSessionEnding(e);
    }
}
