namespace MouseWare.Core;

public enum ActionType
{
    /// <summary>Keep the button's original behaviour (no mapping).</summary>
    Default,
    /// <summary>Swallow the button; nothing happens.</summary>
    Disabled,
    /// <summary>Act as another mouse button (press and release follow the physical button, so dragging works).</summary>
    MouseButton,
    /// <summary>Left double click.</summary>
    DoubleClick,
    /// <summary>Keyboard shortcut, held for as long as the button is held.</summary>
    KeyCombo,
    /// <summary>Scroll vertically or horizontally.</summary>
    Scroll,
    /// <summary>Start a program or open a file/folder.</summary>
    Run,
    /// <summary>Open a web address in the default browser.</summary>
    OpenUrl,
    /// <summary>Type a piece of text.</summary>
    TypeText,
    /// <summary>Act on the window under the cursor.</summary>
    Window,
    /// <summary>Run a small script of keys, clicks, text and delays.</summary>
    Macro,
    /// <summary>Hold the button and move the mouse in one of 8 directions.</summary>
    Gesture,
    /// <summary>Turn MouseWare's mappings on or off.</summary>
    ToggleEnabled,
}

public enum ScrollDirection
{
    Up,
    Down,
    Left,
    Right,
}

public enum WindowCommand
{
    Minimize,
    MaximizeRestore,
    Close,
    ToggleTopmost,
}

public enum GestureDirection
{
    /// <summary>Released without moving far enough.</summary>
    Click,
    Up,
    UpRight,
    Right,
    DownRight,
    Down,
    DownLeft,
    Left,
    UpLeft,
}

/// <summary>
/// What happens when a trigger fires. One flat class (instead of a type hierarchy)
/// keeps the JSON config file simple to read and edit by hand.
/// </summary>
public sealed class ActionDefinition
{
    public ActionType Type { get; set; }

    /// <summary>Optional id of the preset this action came from (used only for display).</summary>
    public string? Preset { get; set; }

    public MouseButtonKind? Button { get; set; }
    public string? Keys { get; set; }
    public ScrollDirection? Scroll { get; set; }
    public int? Amount { get; set; }
    public string? Path { get; set; }
    public string? Arguments { get; set; }
    public string? Url { get; set; }
    public string? Text { get; set; }
    public string? Macro { get; set; }
    public WindowCommand? Window { get; set; }
    public Dictionary<GestureDirection, ActionDefinition>? Gestures { get; set; }

    /// <summary>True for actions that follow the button: pressed on button down, released on button up.</summary>
    public bool IsHoldable => Type is ActionType.MouseButton or ActionType.KeyCombo;

    public ActionDefinition Clone()
    {
        var copy = (ActionDefinition)MemberwiseClone();
        if (Gestures != null)
        {
            copy.Gestures = new Dictionary<GestureDirection, ActionDefinition>();
            foreach (var (dir, action) in Gestures)
                copy.Gestures[dir] = action.Clone();
        }
        return copy;
    }

    public static ActionDefinition OfType(ActionType type) => new() { Type = type };

    public static ActionDefinition Key(string keys, string? preset = null) =>
        new() { Type = ActionType.KeyCombo, Keys = keys, Preset = preset };

    public static ActionDefinition Mouse(MouseButtonKind button) =>
        new() { Type = ActionType.MouseButton, Button = button };

    public static ActionDefinition ScrollBy(ScrollDirection direction, int amount = 1) =>
        new() { Type = ActionType.Scroll, Scroll = direction, Amount = amount };

    public static ActionDefinition WindowAction(WindowCommand command, string? preset = null) =>
        new() { Type = ActionType.Window, Window = command, Preset = preset };
}
