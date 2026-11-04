using System.Globalization;

namespace MouseWare.Core;

/// <summary>Physical mouse buttons that Windows reports to applications.</summary>
public enum MouseButtonKind
{
    Left,
    Right,
    Middle,
    X1,
    X2,
}

/// <summary>
/// Trigger identifiers. A trigger is the physical input a mapping is attached to.
/// They are stored as strings in the config file so new kinds (keyboard keys sent by
/// extra mouse buttons) can be added without breaking old files.
/// </summary>
public static class Triggers
{
    public const string Left = "Left";
    public const string Right = "Right";
    public const string Middle = "Middle";
    public const string X1 = "X1";
    public const string X2 = "X2";
    public const string WheelUp = "WheelUp";
    public const string WheelDown = "WheelDown";
    public const string WheelLeft = "WheelLeft";
    public const string WheelRight = "WheelRight";

    /// <summary>Prefix of keyboard triggers, e.g. "Key:124" for F13.</summary>
    public const string KeyPrefix = "Key:";

    /// <summary>Mouse triggers in the order they are shown to the user.</summary>
    public static readonly IReadOnlyList<string> MouseTriggers = new[]
    {
        Left, Right, Middle, WheelUp, WheelDown, X1, X2, WheelLeft, WheelRight,
    };

    public static string ForKey(int vk) => KeyPrefix + vk.ToString(CultureInfo.InvariantCulture);

    public static bool TryGetKey(string trigger, out int vk)
    {
        vk = 0;
        if (!trigger.StartsWith(KeyPrefix, StringComparison.OrdinalIgnoreCase))
            return false;
        return int.TryParse(trigger.AsSpan(KeyPrefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out vk)
            && vk is > 0 and < 256;
    }

    public static bool IsKey(string trigger) => TryGetKey(trigger, out _);

    public static bool IsWheel(string trigger) =>
        trigger is WheelUp or WheelDown or WheelLeft or WheelRight;

    public static bool IsValid(string trigger) =>
        MouseTriggers.Contains(trigger) || IsKey(trigger);

    public static MouseButtonKind? ToButton(string trigger) => trigger switch
    {
        Left => MouseButtonKind.Left,
        Right => MouseButtonKind.Right,
        Middle => MouseButtonKind.Middle,
        X1 => MouseButtonKind.X1,
        X2 => MouseButtonKind.X2,
        _ => null,
    };

    public static string FromButton(MouseButtonKind button) => button switch
    {
        MouseButtonKind.Left => Left,
        MouseButtonKind.Right => Right,
        MouseButtonKind.Middle => Middle,
        MouseButtonKind.X1 => X1,
        MouseButtonKind.X2 => X2,
        _ => throw new ArgumentOutOfRangeException(nameof(button)),
    };
}
