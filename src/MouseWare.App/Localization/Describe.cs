using System;
using System.Linq;
using MouseWare.Core;

namespace MouseWare.App.Localization;

/// <summary>Human-readable names for triggers and actions in the current language.</summary>
internal static class Describe
{
    public static string Trigger(string trigger)
    {
        if (Triggers.TryGetKey(trigger, out var vk))
            return Loc.F("Trigger_Key", KeyNames.Name(vk));
        return Loc.T("Trigger_" + trigger);
    }

    public static string Button(MouseButtonKind button) => Loc.T("Button_" + button);

    public static string Scroll(ScrollDirection direction) => Loc.T("Scroll_" + direction);

    public static string Window(WindowCommand command) => Loc.T("Window_" + command);

    public static string Gesture(GestureDirection direction) => Loc.T("Gesture_" + direction);

    public static string Preset(Preset preset) => Loc.T("Preset_" + preset.Id);

    public static string PresetCategory(PresetCategory category) => Loc.T("PresetCat_" + category);

    public static string Action(ActionDefinition? action)
    {
        if (action == null)
            return Loc.T("Desc_Default");

        var preset = Presets.Find(action.Preset);
        if (preset != null && preset.Action.Type == action.Type)
        {
            var name = Preset(preset);
            return action.Type == ActionType.KeyCombo && !string.Equals(name, action.Keys, StringComparison.OrdinalIgnoreCase)
                ? $"{name}  ({action.Keys})"
                : name;
        }

        return action.Type switch
        {
            ActionType.Default => Loc.T("Desc_Default"),
            ActionType.Disabled => Loc.T("Desc_Disabled"),
            ActionType.KeyCombo => Loc.F("Desc_KeyCombo", action.Keys),
            ActionType.MouseButton => Loc.F("Desc_MouseButton", Button(action.Button ?? MouseButtonKind.Left)),
            ActionType.DoubleClick => Loc.T("Desc_DoubleClick"),
            ActionType.Scroll => Loc.F("Desc_Scroll", Scroll(action.Scroll ?? ScrollDirection.Down), action.Amount ?? 1),
            ActionType.Run => Loc.F("Desc_Run", System.IO.Path.GetFileName(action.Path?.TrimEnd('\\', '/') ?? "") is { Length: > 0 } f ? f : action.Path),
            ActionType.OpenUrl => Loc.F("Desc_OpenUrl", action.Url),
            ActionType.TypeText => Loc.F("Desc_TypeText", Shorten(action.Text ?? "", 30)),
            ActionType.Window => Loc.F("Desc_Window", Window(action.Window ?? WindowCommand.Minimize)),
            ActionType.Macro => Loc.F("Desc_Macro", MacroParser.Parse(action.Macro).Steps.Count),
            ActionType.Gesture => Loc.F("Desc_Gesture", action.Gestures?.Count(g => g.Value.Type != ActionType.Default) ?? 0),
            ActionType.ToggleEnabled => Loc.T("Desc_ToggleEnabled"),
            _ => action.Type.ToString(),
        };
    }

    public static string MacroError(MacroError error) =>
        Loc.F("Macro_Error", error.Line, Loc.F("MacroErr_" + error.Kind, error.Token));

    private static string Shorten(string text, int max)
    {
        var single = text.Replace("\r", "").Replace('\n', ' ');
        return single.Length <= max ? single : single[..max] + "…";
    }
}
