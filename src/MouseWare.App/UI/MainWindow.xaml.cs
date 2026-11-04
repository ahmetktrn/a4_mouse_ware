using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.Win32;
using MouseWare.App.Localization;
using MouseWare.App.Services;
using MouseWare.Core;

namespace MouseWare.App.UI;

public sealed class MappingRow
{
    public string Trigger { get; init; } = "";
    public string Name { get; init; } = "";
    public string ActionText { get; init; } = "";
    public string Note { get; init; } = "";
    public Visibility NoteVisibility => string.IsNullOrEmpty(Note) ? Visibility.Collapsed : Visibility.Visible;
    public bool CanReset { get; init; }
    public string ChangeText { get; init; } = "";
    public string ResetText { get; init; } = "";
}

public sealed class ProfileItem
{
    public ProfileItem(Profile profile, string display)
    {
        Profile = profile;
        Display = display;
    }

    public Profile Profile { get; }
    public string Display { get; }
}

public partial class MainWindow : Window
{
    public const int TestTabIndex = 1;
    private const int MaxLogLines = 600;
    private const string RepoUrl = "https://github.com/ahmetktrn/a4_mouse_ware";

    private readonly ObservableCollection<string> _log = new();
    private RawInputMonitor? _raw;
    private Profile? _selected;
    private int? _lastKeyVk;
    private bool _closingForExit;
    private bool _loading;

    public MainWindow()
    {
        InitializeComponent();
        TestLog.ItemsSource = _log;

        Loc.Instance.LanguageChanged += OnLanguageChanged;
        App.Current.Hook.InputObserved += OnInputObserved;
        IsVisibleChanged += (_, _) => UpdateMonitoring();
        SourceInitialized += (_, _) =>
        {
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _raw = new RawInputMonitor(source);
            _raw.Observed += AddLog;
        };

        LoadSettings();
        RefreshProfiles(null);
        RefreshEnabled();
    }

    private static AppConfig Config => App.Current.Config.Current;

    private bool SelectedIsDefault => _selected == null || ReferenceEquals(_selected, Config.DefaultProfile);

    public void SelectTab(int index) => Tabs.SelectedIndex = index;

    public void RefreshEnabled() => EnabledToggle.IsChecked = Config.Enabled;

    public void CloseForExit()
    {
        _closingForExit = true;
        Close();
    }

    private static void Save()
    {
        if (!App.Current.Config.Save())
            MessageBox.Show(Loc.T("Settings_SaveFailed"), "MouseWare", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    // ===================== Profiles & mappings =====================

    private void RefreshProfiles(Profile? select)
    {
        var items = Config.Profiles
            .Select((p, i) => new ProfileItem(p, i == 0 ? Loc.T("Profile_Default") : p.Name))
            .ToList();
        ProfileList.ItemsSource = items;
        ProfileList.SelectedItem = items.FirstOrDefault(i => ReferenceEquals(i.Profile, select)) ?? items[0];
    }

    private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = (ProfileList.SelectedItem as ProfileItem)?.Profile ?? Config.DefaultProfile;
        EditProfileButton.IsEnabled = !SelectedIsDefault;
        RemoveProfileButton.IsEnabled = !SelectedIsDefault;
        RefreshMappings();
    }

    private void RefreshMappings()
    {
        var profile = _selected ?? Config.DefaultProfile;
        var defaults = Config.DefaultProfile;
        bool isDefault = SelectedIsDefault;

        ProfileHint.Text = isDefault
            ? Loc.T("Mappings_HintDefault")
            : Loc.F("Mappings_HintApp", string.Join(", ", profile.Processes));

        var keyTriggers = defaults.Mappings.Keys.Concat(profile.Mappings.Keys)
            .Where(Core.Triggers.IsKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => Core.Triggers.TryGetKey(t, out var vk) ? vk : 0);

        var rows = new List<MappingRow>();
        foreach (var trigger in Core.Triggers.MouseTriggers.Concat(keyTriggers))
        {
            profile.Mappings.TryGetValue(trigger, out var own);
            ActionDefinition? inherited = null;
            if (own == null && !isDefault)
                defaults.Mappings.TryGetValue(trigger, out inherited);

            bool removableKey = isDefault && Core.Triggers.IsKey(trigger);
            rows.Add(new MappingRow
            {
                Trigger = trigger,
                Name = Describe.Trigger(trigger),
                ActionText = Describe.Action(own ?? inherited),
                Note = inherited != null ? Loc.T("Mapping_Inherited") : "",
                CanReset = own != null,
                ChangeText = Loc.T("Mapping_Change"),
                ResetText = Loc.T(removableKey ? "Mapping_Remove" : "Mapping_Reset"),
            });
        }
        MappingList.ItemsSource = rows;
    }

    private void ChangeMapping_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string trigger })
            return;
        var profile = _selected ?? Config.DefaultProfile;
        profile.Mappings.TryGetValue(trigger, out var current);

        var editor = new ActionEditorWindow(trigger, current?.Clone(), isAppProfile: !SelectedIsDefault) { Owner = this };
        if (editor.ShowDialog() != true || editor.Result == null)
            return;

        var result = editor.Result;
        if (SelectedIsDefault && result.Type == ActionType.Default && !Core.Triggers.IsKey(trigger))
            profile.Mappings.Remove(trigger); // "original" in the default profile = no mapping
        else
            profile.Mappings[trigger] = result;

        Save();
        RefreshMappings();
    }

    private void ResetMapping_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string trigger })
            return;
        (_selected ?? Config.DefaultProfile).Mappings.Remove(trigger);
        Save();
        RefreshMappings();
    }

    private void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ProfileWindow(null) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        var profile = new Profile { Name = dialog.ProfileName, Processes = dialog.Processes.ToList() };
        Config.Profiles.Add(profile);
        Save();
        RefreshProfiles(profile);
    }

    private void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedIsDefault || _selected == null)
            return;
        var dialog = new ProfileWindow(_selected) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        _selected.Name = dialog.ProfileName;
        _selected.Processes = dialog.Processes.ToList();
        Save();
        RefreshProfiles(_selected);
    }

    private void RemoveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedIsDefault || _selected == null)
            return;
        if (MessageBox.Show(Loc.F("Profile_RemoveConfirm", _selected.Name), "MouseWare",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        Config.Profiles.Remove(_selected);
        Save();
        RefreshProfiles(null);
    }

    private void EnabledToggle_Click(object sender, RoutedEventArgs e) =>
        App.Current.SetEnabled(EnabledToggle.IsChecked == true, notify: false);

    // ===================== Button test =====================

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // SelectionChanged bubbles up from inner lists; only react to the tab control itself.
        if (!ReferenceEquals(e.OriginalSource, Tabs))
            return;
        UpdateMonitoring();
    }

    private void UpdateMonitoring()
    {
        bool on = IsVisible && Tabs.SelectedIndex == TestTabIndex;
        var hook = App.Current.Hook;
        hook.MappingPaused = on && PauseMappingCheck.IsChecked == true;
        hook.Monitoring = on;
        if (_raw == null)
            return;
        if (on)
            _raw.Start();
        else
            _raw.Stop();
    }

    private void PauseMapping_Click(object sender, RoutedEventArgs e) => UpdateMonitoring();

    /// <summary>Called on the hook thread: only hand the event over to the UI thread.</summary>
    private void OnInputObserved(HookEvent e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (e.Source == "keyboard" && e.IsDown && !e.Injected)
                _lastKeyVk = e.VirtualKey;

            string state = Core.Triggers.IsWheel(e.Trigger) ? Loc.T("Test_Wheel") : Loc.T(e.IsDown ? "Test_Down" : "Test_Up");
            string line = $"[hook] {Describe.Trigger(e.Trigger)} {state}"
                + (e.Injected ? Loc.T("Test_Injected") : "")
                + (e.Handled ? Loc.T("Test_Handled") : "")
                + (string.IsNullOrEmpty(e.Process) ? "" : $"  ({e.Process})");
            AddLog(line);
        });
    }

    private void AddLog(string line)
    {
        _log.Add($"{DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)}  {line}");
        while (_log.Count > MaxLogLines)
            _log.RemoveAt(0);
        TestLog.ScrollIntoView(_log[^1]);
    }

    private void ClearTest_Click(object sender, RoutedEventArgs e)
    {
        _log.Clear();
        TestStatus.Text = "";
    }

    private void CopyTest_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, _log));
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            Log.Error("Clipboard copy failed", ex);
        }
    }

    private void ListDevices_Click(object sender, RoutedEventArgs e)
    {
        if (_raw == null)
            return;
        foreach (var line in _raw.DescribeDevices())
            AddLog("[device] " + line);
    }

    private void AddKeyTrigger_Click(object sender, RoutedEventArgs e)
    {
        if (_lastKeyVk is not { } vk)
        {
            TestStatus.Text = Loc.T("Test_NoKey");
            return;
        }
        var trigger = Core.Triggers.ForKey(vk);
        if (!Config.DefaultProfile.Mappings.ContainsKey(trigger))
            Config.DefaultProfile.Mappings[trigger] = ActionDefinition.OfType(ActionType.Default);
        Save();
        RefreshMappings();
        TestStatus.Text = Loc.F("Test_KeyAdded", KeyNames.Name(vk));
    }

    // ===================== Settings =====================

    private void LoadSettings()
    {
        _loading = true;
        LanguageCombo.SelectedIndex = Config.Language == "en" ? 1 : 0;
        try
        {
            StartupCheck.IsChecked = StartupManager.IsEnabled;
        }
        catch (Exception ex)
        {
            Log.Error("Could not read start-up setting", ex);
        }
        GestureSlider.Value = Math.Clamp(Config.GestureThreshold, (int)GestureSlider.Minimum, (int)GestureSlider.Maximum);
        ConfigPathText.Text = AppPaths.ConfigFile;
        UpdateTexts();
        _loading = false;
    }

    private void UpdateTexts()
    {
        GestureLabel.Text = Loc.F("Settings_Gesture", (int)GestureSlider.Value);
        bool admin = Elevation.IsAdministrator;
        AdminText.Text = Loc.T(admin ? "Settings_Admin_Elevated" : "Settings_Admin_Normal");
        AdminButton.Visibility = admin ? Visibility.Collapsed : Visibility.Visible;
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = Loc.F("About_Version", version == null ? "?" : $"{version.Major}.{version.Minor}.{version.Build}");
    }

    private void OnLanguageChanged()
    {
        UpdateTexts();
        RefreshProfiles(_selected);
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageCombo.SelectedItem is not ComboBoxItem { Tag: string language })
            return;
        if (Config.Language == language)
            return;
        Config.Language = language;
        Save();
    }

    private void StartupCheck_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StartupManager.SetEnabled(StartupCheck.IsChecked == true);
        }
        catch (Exception ex)
        {
            Log.Error("Could not change start-up setting", ex);
            StartupCheck.IsChecked = StartupManager.IsEnabled;
        }
    }

    private void GestureSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (GestureLabel != null)
            GestureLabel.Text = Loc.F("Settings_Gesture", (int)GestureSlider.Value);
    }

    private void GestureSlider_Commit(object sender, EventArgs e)
    {
        int value = (int)GestureSlider.Value;
        if (_loading || Config.GestureThreshold == value)
            return;
        Config.GestureThreshold = value;
        Save();
    }

    private void RestartAdmin_Click(object sender, RoutedEventArgs e) => App.Current.RestartAsAdministrator();

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.DataFolder);
        Process.Start(new ProcessStartInfo(AppPaths.DataFolder) { UseShellExecute = true })?.Dispose();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = Loc.T("File_Filter"), FileName = "mouseware-config.json" };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            ConfigStore.Save(dialog.FileName, Config);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "MouseWare", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = Loc.T("File_Filter") };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            var imported = ConfigStore.Deserialize(File.ReadAllText(dialog.FileName));
            App.Current.Config.Replace(imported);
            LoadSettings();
            RefreshProfiles(null);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.F("Settings_ImportFailed", ex.Message), "MouseWare", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(Loc.T("Settings_ResetConfirm"), "MouseWare", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        var fresh = AppConfig.CreateDefault();
        fresh.Language = Config.Language;
        App.Current.Config.Replace(fresh);
        LoadSettings();
        RefreshProfiles(null);
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        if (File.Exists(Log.FilePath))
            Process.Start(new ProcessStartInfo(Log.FilePath) { UseShellExecute = true })?.Dispose();
    }

    private void OpenRepo_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(RepoUrl) { UseShellExecute = true })?.Dispose();

    // ===================== Window lifetime =====================

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_closingForExit)
        {
            // Closing the window keeps MouseWare running in the notification area.
            e.Cancel = true;
            Hide();
            App.Current.OnMainHidden();
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        Loc.Instance.LanguageChanged -= OnLanguageChanged;
        App.Current.Hook.InputObserved -= OnInputObserved;
        App.Current.Hook.Monitoring = false;
        App.Current.Hook.MappingPaused = false;
        _raw?.Dispose();
        base.OnClosed(e);
    }
}
