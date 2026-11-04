using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using MouseWare.App.Localization;
using MouseWare.Core;

namespace MouseWare.App.UI;

public partial class ActionEditorWindow : Window
{
    private enum Kind
    {
        Default,
        Disabled,
        Preset,
        KeyCombo,
        MouseButton,
        DoubleClick,
        Scroll,
        Run,
        OpenUrl,
        TypeText,
        Window,
        Macro,
        Gesture,
        ToggleEnabled,
    }

    private sealed record KindItem(Kind Kind, string Display);

    public sealed record PresetItem(Preset Preset, string Display, string Category);

    private static readonly GestureDirection[] GestureLayout =
    {
        GestureDirection.UpLeft, GestureDirection.Up, GestureDirection.UpRight,
        GestureDirection.Left, GestureDirection.Click, GestureDirection.Right,
        GestureDirection.DownLeft, GestureDirection.Down, GestureDirection.DownRight,
    };

    private readonly string _trigger;
    private readonly bool _isAppProfile;
    private readonly bool _isTopLevel;
    private readonly Dictionary<GestureDirection, ActionDefinition> _gestures = new();
    private readonly HashSet<int> _pressedModifiers = new();
    private bool _recording;
    private string _textBeforeRecording = "";

    public ActionDefinition? Result { get; private set; }

    public ActionEditorWindow(string trigger, ActionDefinition? current, bool isAppProfile,
        bool isTopLevel = true, string? title = null)
    {
        InitializeComponent();
        _trigger = trigger;
        _isAppProfile = isAppProfile;
        _isTopLevel = isTopLevel;
        Title = Loc.F("Editor_Title", title ?? Describe.Trigger(trigger));

        var kinds = Enum.GetValues<Kind>().AsEnumerable();
        if (!isTopLevel || Core.Triggers.IsWheel(trigger))
            kinds = kinds.Where(k => k != Kind.Gesture); // gestures need a button that is held
        TypeList.ItemsSource = kinds.Select(k => new KindItem(k, Loc.T("ActionType_" + k))).ToList();

        MouseCombo.ItemsSource = Enum.GetValues<MouseButtonKind>().Select(b => Item(Describe.Button(b), b)).ToList();
        ScrollDirCombo.ItemsSource = Enum.GetValues<ScrollDirection>().Select(d => Item(Describe.Scroll(d), d)).ToList();
        WindowCombo.ItemsSource = Enum.GetValues<WindowCommand>().Select(w => Item(Describe.Window(w), w)).ToList();
        SpecialKeyCombo.ItemsSource = KeyNames.AllNames.ToList();

        var presets = Presets.All
            .Select(p => new PresetItem(p, Describe.Preset(p), Describe.PresetCategory(p.Category)))
            .ToList();
        var view = CollectionViewSource.GetDefaultView(presets);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PresetItem.Category)));
        PresetList.ItemsSource = view;

        Load(current);
    }

    private static ComboBoxItem Item(string text, object tag) => new() { Content = text, Tag = tag };

    private static void SelectTag(ComboBox combo, object? tag)
    {
        var items = combo.ItemsSource.Cast<ComboBoxItem>().ToList();
        combo.SelectedItem = items.FirstOrDefault(i => Equals(i.Tag, tag)) ?? items[0];
    }

    private static T? SelectedTag<T>(ComboBox combo) where T : struct =>
        (combo.SelectedItem as ComboBoxItem)?.Tag is T value ? value : null;

    // ---------------- Loading ----------------

    private void Load(ActionDefinition? action)
    {
        action ??= ActionDefinition.OfType(ActionType.Default);

        KeysBox.Text = action.Keys ?? "";
        SelectTag(MouseCombo, action.Button ?? MouseButtonKind.Left);
        SelectTag(ScrollDirCombo, action.Scroll ?? ScrollDirection.Down);
        ScrollAmountBox.Text = (action.Amount ?? 1).ToString(CultureInfo.InvariantCulture);
        RunPathBox.Text = action.Path ?? "";
        RunArgsBox.Text = action.Arguments ?? "";
        UrlBox.Text = action.Url ?? "";
        TypeTextBox.Text = action.Text ?? "";
        SelectTag(WindowCombo, action.Window ?? WindowCommand.Minimize);
        MacroBox.Text = action.Macro ?? "";
        if (action.Gestures != null)
        {
            foreach (var (direction, sub) in action.Gestures)
                _gestures[direction] = sub.Clone();
        }

        var preset = Presets.Find(action.Preset);
        Kind kind = preset != null && preset.Action.Type == action.Type
            ? Kind.Preset
            : Enum.TryParse<Kind>(action.Type.ToString(), out var k) ? k : Kind.Default;

        if (kind == Kind.Preset)
        {
            PresetList.SelectedItem = PresetList.Items.Cast<PresetItem>().FirstOrDefault(i => i.Preset.Id == preset!.Id);
            PresetList.ScrollIntoView(PresetList.SelectedItem);
        }

        var items = TypeList.ItemsSource.Cast<KindItem>().ToList();
        TypeList.SelectedItem = items.FirstOrDefault(i => i.Kind == kind) ?? items[0];
    }

    private Kind SelectedKind => (TypeList.SelectedItem as KindItem)?.Kind ?? Kind.Default;

    private void TypeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        StopRecording(keepText: false);
        ValidationText.Text = "";
        foreach (var panel in new FrameworkElement[] { PanelInfo, PanelPreset, PanelKeys, PanelMouse, PanelScroll, PanelRun, PanelUrl, PanelText, PanelWindow, PanelMacro, PanelGesture })
            panel.Visibility = Visibility.Collapsed;

        FrameworkElement? show = SelectedKind switch
        {
            Kind.Preset => PanelPreset,
            Kind.KeyCombo => PanelKeys,
            Kind.MouseButton => PanelMouse,
            Kind.Scroll => PanelScroll,
            Kind.Run => PanelRun,
            Kind.OpenUrl => PanelUrl,
            Kind.TypeText => PanelText,
            Kind.Window => PanelWindow,
            Kind.Macro => PanelMacro,
            Kind.Gesture => PanelGesture,
            _ => PanelInfo,
        };
        show.Visibility = Visibility.Visible;

        PanelInfo.Text = SelectedKind switch
        {
            Kind.Default => Loc.T(_isAppProfile ? "Info_DefaultApp" : "Info_Default"),
            Kind.Disabled => Loc.T("Info_Disabled"),
            Kind.DoubleClick => Loc.T("Info_DoubleClick"),
            Kind.ToggleEnabled => Loc.T("Info_Toggle"),
            _ => "",
        };

        if (SelectedKind == Kind.Gesture)
            RefreshGestureButtons();
        if (SelectedKind == Kind.Macro)
            ValidateMacro();
        if (SelectedKind == Kind.KeyCombo)
            ValidateKeys();
    }

    // ---------------- Building the result ----------------

    private ActionDefinition? Build()
    {
        switch (SelectedKind)
        {
            case Kind.Default: return ActionDefinition.OfType(ActionType.Default);
            case Kind.Disabled: return ActionDefinition.OfType(ActionType.Disabled);
            case Kind.DoubleClick: return ActionDefinition.OfType(ActionType.DoubleClick);
            case Kind.ToggleEnabled: return ActionDefinition.OfType(ActionType.ToggleEnabled);

            case Kind.Preset:
                if (PresetList.SelectedItem is not PresetItem item)
                {
                    ValidationText.Text = Loc.T("Editor_PresetSelect");
                    return null;
                }
                return item.Preset.Action.Clone();

            case Kind.KeyCombo:
                if (!KeyCombo.TryParse(KeysBox.Text, out var combo))
                    return null;
                return ActionDefinition.Key(combo.ToString());

            case Kind.MouseButton:
                return ActionDefinition.Mouse(SelectedTag<MouseButtonKind>(MouseCombo) ?? MouseButtonKind.Left);

            case Kind.Scroll:
                if (!int.TryParse(ScrollAmountBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount) || amount < 1 || amount > 50)
                    return null;
                return ActionDefinition.ScrollBy(SelectedTag<ScrollDirection>(ScrollDirCombo) ?? ScrollDirection.Down, amount);

            case Kind.Run:
                if (string.IsNullOrWhiteSpace(RunPathBox.Text))
                    return null;
                return new ActionDefinition
                {
                    Type = ActionType.Run,
                    Path = RunPathBox.Text.Trim(),
                    Arguments = string.IsNullOrWhiteSpace(RunArgsBox.Text) ? null : RunArgsBox.Text.Trim(),
                };

            case Kind.OpenUrl:
                if (string.IsNullOrWhiteSpace(UrlBox.Text))
                    return null;
                return new ActionDefinition { Type = ActionType.OpenUrl, Url = UrlBox.Text.Trim() };

            case Kind.TypeText:
                if (string.IsNullOrEmpty(TypeTextBox.Text))
                    return null;
                return new ActionDefinition { Type = ActionType.TypeText, Text = TypeTextBox.Text };

            case Kind.Window:
                return ActionDefinition.WindowAction(SelectedTag<WindowCommand>(WindowCombo) ?? WindowCommand.Minimize);

            case Kind.Macro:
                var parsed = MacroParser.Parse(MacroBox.Text);
                if (!parsed.Success || parsed.Steps.Count == 0)
                    return null;
                return new ActionDefinition { Type = ActionType.Macro, Macro = MacroBox.Text };

            case Kind.Gesture:
                return new ActionDefinition
                {
                    Type = ActionType.Gesture,
                    Gestures = _gestures.ToDictionary(kv => kv.Key, kv => kv.Value.Clone()),
                };
        }
        return null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        StopRecording(keepText: true);
        var result = Build();
        if (result == null)
        {
            if (string.IsNullOrEmpty(ValidationText.Text))
                ValidationText.Text = Loc.T("Editor_Validation");
            return;
        }

        if (_isTopLevel && _trigger == Core.Triggers.Left && result.Type != ActionType.Default
            && MessageBox.Show(this, Loc.T("Editor_LeftWarning"), "MouseWare", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        Result = result;
        DialogResult = true;
    }

    // ---------------- Presets ----------------

    private void PresetList_SelectionChanged(object sender, SelectionChangedEventArgs e) => ValidationText.Text = "";

    // ---------------- Keyboard shortcut ----------------

    private void KeysBox_TextChanged(object sender, TextChangedEventArgs e) => ValidateKeys();

    private void ValidateKeys()
    {
        if (_recording)
        {
            KeysError.Text = "";
            return;
        }
        KeysError.Text = KeysBox.Text.Length == 0 || KeyCombo.TryParse(KeysBox.Text, out _) ? "" : Loc.T("Keys_Invalid");
    }

    private void RecordButton_Checked(object sender, RoutedEventArgs e)
    {
        _recording = true;
        _pressedModifiers.Clear();
        _textBeforeRecording = KeysBox.Text;
        KeysBox.Text = Loc.T("Keys_Recording");
        KeysError.Text = "";
    }

    private void RecordButton_Unchecked(object sender, RoutedEventArgs e) => StopRecording(keepText: false);

    private void StopRecording(bool keepText)
    {
        if (!_recording)
            return;
        _recording = false;
        _pressedModifiers.Clear();
        if (!keepText || KeysBox.Text == Loc.T("Keys_Recording"))
            KeysBox.Text = _textBeforeRecording;
        RecordButton.IsChecked = false;
        ValidateKeys();
    }

    private void FinishRecording(KeyCombo combo)
    {
        _recording = false;
        _pressedModifiers.Clear();
        KeysBox.Text = combo.ToString();
        RecordButton.IsChecked = false;
        ValidateKeys();
    }

    private static int VirtualKey(KeyEventArgs e)
    {
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key,
        };
        return KeyInterop.VirtualKeyFromKey(key);
    }

    private static IEnumerable<int> HeldModifiers()
    {
        var modifiers = Keyboard.Modifiers;
        if (modifiers.HasFlag(ModifierKeys.Control))
            yield return KeyNames.Control;
        if (modifiers.HasFlag(ModifierKeys.Alt))
            yield return KeyNames.Alt;
        if (modifiers.HasFlag(ModifierKeys.Shift))
            yield return KeyNames.Shift;
        if (modifiers.HasFlag(ModifierKeys.Windows))
            yield return KeyNames.Win;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_recording)
            return;
        e.Handled = true;
        int vk = VirtualKey(e);
        if (vk == 0)
            return;

        if (KeyNames.IsModifier(vk))
        {
            _pressedModifiers.Add(KeyNames.CanonicalModifier(vk));
            var order = new[] { KeyNames.Control, KeyNames.Alt, KeyNames.Shift, KeyNames.Win };
            KeysBox.Text = string.Join("+", order.Where(_pressedModifiers.Contains).Select(KeyNames.Name)) + "+…";
            return;
        }

        FinishRecording(KeyCombo.FromPressed(HeldModifiers().Concat(_pressedModifiers), vk));
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (!_recording)
            return;
        e.Handled = true;
        int vk = VirtualKey(e);
        if (!KeyNames.IsModifier(vk) || _pressedModifiers.Count == 0)
            return;

        // Only modifiers were pressed (e.g. "Ctrl" alone, or "Win"): record them on release.
        var released = KeyNames.CanonicalModifier(vk);
        FinishRecording(KeyCombo.FromPressed(_pressedModifiers.Where(m => m != released), released));
    }

    private void AddSpecial_Click(object sender, RoutedEventArgs e)
    {
        if (SpecialKeyCombo.SelectedItem is not string name)
            return;
        StopRecording(keepText: true);
        if (KeyCombo.TryParse(KeysBox.Text, out var existing) && KeyNames.TryGetVk(name, out var vk))
        {
            // Keep the modifiers already typed and replace the main key.
            var modifiers = KeyNames.IsModifier(existing.Key)
                ? existing.Modifiers.Append(existing.Key)
                : existing.Modifiers;
            KeysBox.Text = KeyCombo.FromPressed(modifiers, vk).ToString();
        }
        else
        {
            KeysBox.Text = name;
        }
    }

    // ---------------- Run ----------------

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Programs (*.exe;*.bat;*.cmd;*.lnk)|*.exe;*.bat;*.cmd;*.lnk|All files (*.*)|*.*",
            DereferenceLinks = false,
        };
        if (dialog.ShowDialog(this) == true)
            RunPathBox.Text = dialog.FileName;
    }

    // ---------------- Macro ----------------

    private void MacroBox_TextChanged(object sender, TextChangedEventArgs e) => ValidateMacro();

    private void ValidateMacro()
    {
        var result = MacroParser.Parse(MacroBox.Text);
        if (result.Success)
        {
            MacroStatus.Foreground = (System.Windows.Media.Brush)FindResource("OkBrush");
            MacroStatus.Text = result.Steps.Count == 0 ? "" : Loc.F("Macro_OK", result.Steps.Count);
        }
        else
        {
            MacroStatus.Foreground = (System.Windows.Media.Brush)FindResource("ErrorBrush");
            MacroStatus.Text = string.Join(Environment.NewLine, result.Errors.Take(4).Select(Describe.MacroError));
        }
    }

    // ---------------- Gesture ----------------

    private void RefreshGestureButtons()
    {
        GestureGrid.Children.Clear();
        foreach (var direction in GestureLayout)
        {
            _gestures.TryGetValue(direction, out var action);
            string summary = action == null || action.Type == ActionType.Default
                ? (direction == GestureDirection.Click ? Loc.T("Desc_Default") : Loc.T("Gesture_None"))
                : Describe.Action(action);

            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            panel.Children.Add(new TextBlock
            {
                Text = Describe.Gesture(direction),
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
            });
            panel.Children.Add(new TextBlock
            {
                Text = summary,
                Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0),
            });

            var button = new Button
            {
                Content = panel,
                Tag = direction,
                Margin = new Thickness(4),
                Padding = new Thickness(6),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            if (direction == GestureDirection.Click)
                button.Background = (System.Windows.Media.Brush)FindResource("AccentSoftBrush");
            button.Click += GestureButton_Click;
            GestureGrid.Children.Add(button);
        }
    }

    private void GestureButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: GestureDirection direction })
            return;
        _gestures.TryGetValue(direction, out var current);
        var editor = new ActionEditorWindow(_trigger, current?.Clone(), isAppProfile: false, isTopLevel: false,
            title: $"{Describe.Trigger(_trigger)} — {Describe.Gesture(direction).Replace('\n', ' ')}")
        {
            Owner = this,
        };
        if (editor.ShowDialog() != true || editor.Result == null)
            return;

        if (editor.Result.Type == ActionType.Default)
            _gestures.Remove(direction);
        else
            _gestures[direction] = editor.Result;
        RefreshGestureButtons();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        StopRecording(keepText: false);
        base.OnClosing(e);
    }
}
