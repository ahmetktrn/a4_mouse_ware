namespace MouseWare.Core;

public enum PresetCategory
{
    Navigation,
    Editing,
    Media,
    Windows,
    Browser,
}

/// <summary>A ready-made action. The UI shows a translated name for <see cref="Id"/>.</summary>
public sealed record Preset(string Id, PresetCategory Category, ActionDefinition Action);

public static class Presets
{
    public static readonly IReadOnlyList<Preset> All = new List<Preset>
    {
        Key("back", PresetCategory.Navigation, "Alt+Left"),
        Key("forward", PresetCategory.Navigation, "Alt+Right"),
        Key("enter", PresetCategory.Navigation, "Enter"),
        Key("escape", PresetCategory.Navigation, "Esc"),
        Key("pageUp", PresetCategory.Navigation, "PageUp"),
        Key("pageDown", PresetCategory.Navigation, "PageDown"),
        Key("home", PresetCategory.Navigation, "Ctrl+Home"),
        Key("end", PresetCategory.Navigation, "Ctrl+End"),

        Key("copy", PresetCategory.Editing, "Ctrl+C"),
        Key("paste", PresetCategory.Editing, "Ctrl+V"),
        Key("cut", PresetCategory.Editing, "Ctrl+X"),
        Key("undo", PresetCategory.Editing, "Ctrl+Z"),
        Key("redo", PresetCategory.Editing, "Ctrl+Y"),
        Key("selectAll", PresetCategory.Editing, "Ctrl+A"),
        Key("save", PresetCategory.Editing, "Ctrl+S"),
        Key("find", PresetCategory.Editing, "Ctrl+F"),
        Key("delete", PresetCategory.Editing, "Delete"),

        Key("playPause", PresetCategory.Media, "MediaPlayPause"),
        Key("nextTrack", PresetCategory.Media, "MediaNext"),
        Key("prevTrack", PresetCategory.Media, "MediaPrev"),
        Key("stop", PresetCategory.Media, "MediaStop"),
        Key("volumeUp", PresetCategory.Media, "VolumeUp"),
        Key("volumeDown", PresetCategory.Media, "VolumeDown"),
        Key("mute", PresetCategory.Media, "VolumeMute"),

        Key("showDesktop", PresetCategory.Windows, "Win+D"),
        Key("taskView", PresetCategory.Windows, "Win+Tab"),
        Key("switchApp", PresetCategory.Windows, "Alt+Tab"),
        Key("nextDesktop", PresetCategory.Windows, "Ctrl+Win+Right"),
        Key("prevDesktop", PresetCategory.Windows, "Ctrl+Win+Left"),
        Key("screenshot", PresetCategory.Windows, "Win+Shift+S"),
        Key("fileExplorer", PresetCategory.Windows, "Win+E"),
        Key("settings", PresetCategory.Windows, "Win+I"),
        Key("startMenu", PresetCategory.Windows, "Win"),
        Key("closeApp", PresetCategory.Windows, "Alt+F4"),
        Win("minimize", WindowCommand.Minimize),
        Win("maximize", WindowCommand.MaximizeRestore),
        Win("closeWindow", WindowCommand.Close),
        Win("alwaysOnTop", WindowCommand.ToggleTopmost),

        Key("newTab", PresetCategory.Browser, "Ctrl+T"),
        Key("closeTab", PresetCategory.Browser, "Ctrl+W"),
        Key("reopenTab", PresetCategory.Browser, "Ctrl+Shift+T"),
        Key("nextTab", PresetCategory.Browser, "Ctrl+Tab"),
        Key("prevTab", PresetCategory.Browser, "Ctrl+Shift+Tab"),
        Key("refresh", PresetCategory.Browser, "F5"),
        Key("zoomIn", PresetCategory.Browser, "Ctrl+Plus"),
        Key("zoomOut", PresetCategory.Browser, "Ctrl+Minus"),
        Key("zoomReset", PresetCategory.Browser, "Ctrl+0"),
    };

    public static Preset? Find(string? id) =>
        id == null ? null : All.FirstOrDefault(p => p.Id == id);

    private static Preset Key(string id, PresetCategory category, string keys) =>
        new(id, category, ActionDefinition.Key(keys, id));

    private static Preset Win(string id, WindowCommand command) =>
        new(id, PresetCategory.Windows, ActionDefinition.WindowAction(command, id));
}
