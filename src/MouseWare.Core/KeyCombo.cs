using System.Globalization;

namespace MouseWare.Core;

/// <summary>Names of Windows virtual-key codes, used in the config file and in the UI.</summary>
public static class KeyNames
{
    public const int Shift = 0x10;
    public const int Control = 0x11;
    public const int Alt = 0x12;
    public const int Win = 0x5B;

    private static readonly Dictionary<string, int> NameToVk = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<int, string> VkToName = new();

    static KeyNames()
    {
        // Canonical names (first registration of a code wins for display).
        Add("Ctrl", Control);
        Add("Alt", Alt);
        Add("Shift", Shift);
        Add("Win", Win);
        Add("Backspace", 0x08);
        Add("Tab", 0x09);
        Add("Enter", 0x0D);
        Add("Pause", 0x13);
        Add("CapsLock", 0x14);
        Add("Esc", 0x1B);
        Add("Space", 0x20);
        Add("PageUp", 0x21);
        Add("PageDown", 0x22);
        Add("End", 0x23);
        Add("Home", 0x24);
        Add("Left", 0x25);
        Add("Up", 0x26);
        Add("Right", 0x27);
        Add("Down", 0x28);
        Add("PrintScreen", 0x2C);
        Add("Insert", 0x2D);
        Add("Delete", 0x2E);
        for (int d = 0; d <= 9; d++)
            Add(d.ToString(CultureInfo.InvariantCulture), 0x30 + d);
        for (char c = 'A'; c <= 'Z'; c++)
            Add(c.ToString(), c);
        Add("RWin", 0x5C);
        Add("Apps", 0x5D);
        for (int n = 0; n <= 9; n++)
            Add("Num" + n.ToString(CultureInfo.InvariantCulture), 0x60 + n);
        Add("NumMultiply", 0x6A);
        Add("NumPlus", 0x6B);
        Add("NumMinus", 0x6D);
        Add("NumDecimal", 0x6E);
        Add("NumDivide", 0x6F);
        for (int f = 1; f <= 24; f++)
            Add("F" + f.ToString(CultureInfo.InvariantCulture), 0x70 + f - 1);
        Add("NumLock", 0x90);
        Add("ScrollLock", 0x91);
        Add("LShift", 0xA0);
        Add("RShift", 0xA1);
        Add("LCtrl", 0xA2);
        Add("RCtrl", 0xA3);
        Add("LAlt", 0xA4);
        Add("RAlt", 0xA5);
        Add("BrowserBack", 0xA6);
        Add("BrowserForward", 0xA7);
        Add("BrowserRefresh", 0xA8);
        Add("BrowserStop", 0xA9);
        Add("BrowserSearch", 0xAA);
        Add("BrowserFavorites", 0xAB);
        Add("BrowserHome", 0xAC);
        Add("VolumeMute", 0xAD);
        Add("VolumeDown", 0xAE);
        Add("VolumeUp", 0xAF);
        Add("MediaNext", 0xB0);
        Add("MediaPrev", 0xB1);
        Add("MediaStop", 0xB2);
        Add("MediaPlayPause", 0xB3);
        Add("LaunchMail", 0xB4);
        Add("LaunchMedia", 0xB5);
        Add("LaunchApp1", 0xB6);
        Add("LaunchApp2", 0xB7);
        Add("Semicolon", 0xBA);
        Add("Plus", 0xBB);
        Add("Comma", 0xBC);
        Add("Minus", 0xBD);
        Add("Period", 0xBE);
        Add("Slash", 0xBF);
        Add("Backtick", 0xC0);
        Add("OpenBracket", 0xDB);
        Add("Backslash", 0xDC);
        Add("CloseBracket", 0xDD);
        Add("Quote", 0xDE);
        Add("Oem102", 0xE2);

        // Aliases accepted when parsing.
        Alias("Control", Control);
        Alias("Ctl", Control);
        Alias("Windows", Win);
        Alias("LWin", Win);
        Alias("Escape", 0x1B);
        Alias("Return", 0x0D);
        Alias("Del", 0x2E);
        Alias("Ins", 0x2D);
        Alias("PgUp", 0x21);
        Alias("PgDn", 0x22);
        Alias("PrtSc", 0x2C);
        Alias("Menu", 0x5D);
        Alias("Mute", 0xAD);
        Alias("PlayPause", 0xB3);
        Alias("NextTrack", 0xB0);
        Alias("PrevTrack", 0xB1);
    }

    private static void Add(string name, int vk)
    {
        NameToVk[name] = vk;
        VkToName.TryAdd(vk, name);
    }

    private static void Alias(string name, int vk) => NameToVk[name] = vk;

    /// <summary>All canonical key names, for pick lists.</summary>
    public static IEnumerable<string> AllNames => VkToName.OrderBy(kv => kv.Key).Select(kv => kv.Value);

    public static string Name(int vk) =>
        VkToName.TryGetValue(vk, out var name) ? name : "VK" + vk.ToString(CultureInfo.InvariantCulture);

    public static bool TryGetVk(string name, out int vk)
    {
        name = name.Trim();
        if (NameToVk.TryGetValue(name, out vk))
            return true;

        // Raw codes: "VK124", "VK0x7C" or "0x7C".
        var raw = name.StartsWith("VK", StringComparison.OrdinalIgnoreCase) ? name[2..] : name;
        if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(raw.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out vk))
            return vk is > 0 and < 256;
        if (name.StartsWith("VK", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out vk))
            return vk is > 0 and < 256;

        vk = 0;
        return false;
    }

    public static bool IsModifier(int vk) =>
        vk is Shift or Control or Alt or Win or 0x5C or (>= 0xA0 and <= 0xA5);

    /// <summary>Left/right variants → generic modifier (LCtrl → Ctrl, RWin → Win).</summary>
    public static int CanonicalModifier(int vk) => vk switch
    {
        0xA0 or 0xA1 => Shift,
        0xA2 or 0xA3 => Control,
        0xA4 or 0xA5 => Alt,
        0x5C => Win,
        _ => vk,
    };

    /// <summary>Keys that need KEYEVENTF_EXTENDEDKEY when sent with SendInput.</summary>
    public static bool IsExtended(int vk) =>
        vk is (>= 0x21 and <= 0x28) // PageUp, PageDown, End, Home, arrows
            or 0x2C or 0x2D or 0x2E // PrintScreen, Insert, Delete
            or 0x5B or 0x5C or 0x5D // Win keys, Apps
            or 0x6F or 0x90 // NumDivide, NumLock
            or 0xA3 or 0xA5 // RCtrl, RAlt
            or (>= 0xA6 and <= 0xB7); // browser, volume, media, launch keys
}

/// <summary>A keyboard shortcut such as Ctrl+Shift+T: zero or more modifiers plus one key.</summary>
public sealed record KeyCombo(IReadOnlyList<int> Modifiers, int Key)
{
    private static readonly int[] ModifierOrder = { KeyNames.Control, KeyNames.Alt, KeyNames.Shift, KeyNames.Win };

    public static bool TryParse(string? text, out KeyCombo combo)
    {
        combo = new KeyCombo(Array.Empty<int>(), 0);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // "Ctrl++" means Ctrl and the plus key.
        var normalized = text.Trim();
        if (normalized.EndsWith("++", StringComparison.Ordinal))
            normalized = normalized[..^2] + "+Plus";
        else if (normalized == "+")
            normalized = "Plus";

        var parts = normalized.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = new List<int>();
        int key = 0;
        foreach (var part in parts)
        {
            if (part.Length == 0 || !KeyNames.TryGetVk(part, out var vk))
                return false;

            if (KeyNames.IsModifier(vk) && key == 0)
            {
                var mod = KeyNames.CanonicalModifier(vk);
                if (modifiers.Contains(mod))
                    return false;
                modifiers.Add(mod);
            }
            else
            {
                if (key != 0)
                    return false; // only one normal key per combo
                key = vk;
            }
        }

        if (key == 0)
        {
            // Only modifiers ("Ctrl" or "Ctrl+Shift"): the last one is the key itself.
            key = modifiers[^1];
            modifiers.RemoveAt(modifiers.Count - 1);
        }

        modifiers.Sort((a, b) => Array.IndexOf(ModifierOrder, a).CompareTo(Array.IndexOf(ModifierOrder, b)));
        combo = new KeyCombo(modifiers, key);
        return true;
    }

    /// <summary>Builds a combo from keys held down at the same time (e.g. captured in the UI).</summary>
    public static KeyCombo FromPressed(IEnumerable<int> modifiers, int key)
    {
        key = KeyNames.CanonicalModifier(key);
        var mods = modifiers.Select(KeyNames.CanonicalModifier).Distinct()
            .Where(m => m != key)
            .OrderBy(m => Array.IndexOf(ModifierOrder, m))
            .ToList();
        return new KeyCombo(mods, key);
    }

    public override string ToString() =>
        string.Join("+", Modifiers.Select(KeyNames.Name).Append(KeyNames.Name(Key)));
}
