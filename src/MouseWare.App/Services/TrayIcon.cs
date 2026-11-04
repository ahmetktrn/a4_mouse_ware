using System;
using System.Windows;
using MouseWare.App.Localization;
using Forms = System.Windows.Forms;

namespace MouseWare.App.Services;

/// <summary>Notification-area icon with a small menu.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _open;
    private readonly Forms.ToolStripMenuItem _enabled;
    private readonly Forms.ToolStripMenuItem _test;
    private readonly Forms.ToolStripMenuItem _exit;
    private bool _isEnabled = true;

    public event Action? OpenRequested;
    public event Action? TestRequested;
    public event Action? ToggleRequested;
    public event Action? ExitRequested;

    public TrayIcon()
    {
        _open = new Forms.ToolStripMenuItem();
        _open.Font = new System.Drawing.Font(_open.Font, System.Drawing.FontStyle.Bold);
        _open.Click += (_, _) => OpenRequested?.Invoke();
        _enabled = new Forms.ToolStripMenuItem { CheckOnClick = false };
        _enabled.Click += (_, _) => ToggleRequested?.Invoke();
        _test = new Forms.ToolStripMenuItem();
        _test.Click += (_, _) => TestRequested?.Invoke();
        _exit = new Forms.ToolStripMenuItem();
        _exit.Click += (_, _) => ExitRequested?.Invoke();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.AddRange(new Forms.ToolStripItem[] { _open, _enabled, _test, new Forms.ToolStripSeparator(), _exit });

        _icon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                OpenRequested?.Invoke();
        };

        Loc.Instance.LanguageChanged += UpdateTexts;
        UpdateTexts();
    }

    public void SetEnabled(bool enabled)
    {
        _isEnabled = enabled;
        UpdateTexts();
    }

    public void ShowBalloon(string title, string text)
    {
        _icon.ShowBalloonTip(3000, title, text, Forms.ToolTipIcon.Info);
    }

    private void UpdateTexts()
    {
        _open.Text = Loc.T("Tray_Open");
        _enabled.Text = Loc.T("Tray_Enabled");
        _enabled.Checked = _isEnabled;
        _test.Text = Loc.T("Tray_Test");
        _exit.Text = Loc.T("Tray_Exit");
        _icon.Text = _isEnabled ? "MouseWare" : "MouseWare — " + Loc.T("Tray_Off");
    }

    private static System.Drawing.Icon LoadIcon()
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/mouseware.ico"));
        if (resource != null)
        {
            using var stream = resource.Stream;
            return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
        }
        return System.Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        Loc.Instance.LanguageChanged -= UpdateTexts;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
