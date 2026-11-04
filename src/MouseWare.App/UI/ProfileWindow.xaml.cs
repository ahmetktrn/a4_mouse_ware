using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using MouseWare.App.Localization;
using MouseWare.App.Services;
using MouseWare.Core;

namespace MouseWare.App.UI;

public partial class ProfileWindow : Window
{
    public string ProfileName { get; private set; } = "";
    public IReadOnlyList<string> Processes { get; private set; } = Array.Empty<string>();

    public ProfileWindow(Profile? profile)
    {
        InitializeComponent();
        if (profile != null)
        {
            NameBox.Text = profile.Name;
            ProcessesBox.Text = string.Join(Environment.NewLine, profile.Processes);
        }
        RunningCombo.ItemsSource = RunningPrograms();
        Loaded += (_, _) => NameBox.Focus();
    }

    /// <summary>Programs that currently have a window, e.g. "chrome.exe".</summary>
    private static List<string> RunningPrograms()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    var name = ProcessResolver.FromProcessId((uint)process.Id) ?? AppConfig.NormalizeProcessName(process.ProcessName);
                    if (!string.IsNullOrEmpty(name))
                        names.Add(name);
                }
            }
            catch (InvalidOperationException)
            {
                // Process exited while we were looking at it.
            }
            finally
            {
                process.Dispose();
            }
        }
        return names.ToList();
    }

    private void AddRunning_Click(object sender, RoutedEventArgs e)
    {
        if (RunningCombo.SelectedItem is not string name)
            return;
        var current = ParseProcesses();
        if (current.Contains(name, StringComparer.OrdinalIgnoreCase))
            return;
        ProcessesBox.Text = string.Join(Environment.NewLine, current.Append(name));
        if (string.IsNullOrWhiteSpace(NameBox.Text))
            NameBox.Text = System.IO.Path.GetFileNameWithoutExtension(name);
    }

    private List<string> ParseProcesses() =>
        ProcessesBox.Text
            .Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(AppConfig.NormalizeProcessName)
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var processes = ParseProcesses();
        if (string.IsNullOrWhiteSpace(NameBox.Text) || processes.Count == 0)
        {
            ValidationText.Text = Loc.T("ProfileWin_Validation");
            return;
        }
        ProfileName = NameBox.Text.Trim();
        Processes = processes;
        DialogResult = true;
    }
}
