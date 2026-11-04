namespace MouseWare.Core;

/// <summary>A set of mappings. The first profile is the default; the others apply to specific programs.</summary>
public sealed class Profile
{
    public string Name { get; set; } = "";

    /// <summary>Program file names this profile applies to, e.g. "chrome.exe". Empty for the default profile.</summary>
    public List<string> Processes { get; set; } = new();

    /// <summary>Trigger id → action. Triggers that are missing keep their behaviour (or inherit from the default profile).</summary>
    public Dictionary<string, ActionDefinition> Mappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool Matches(string? processName)
    {
        if (string.IsNullOrEmpty(processName))
            return false;
        var normalized = AppConfig.NormalizeProcessName(processName);
        return Processes.Any(p => string.Equals(AppConfig.NormalizeProcessName(p), normalized, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class AppConfig
{
    public const int CurrentVersion = 1;
    public const int MinGestureThreshold = 5;
    public const int MaxGestureThreshold = 300;

    public int Version { get; set; } = CurrentVersion;
    public bool Enabled { get; set; } = true;
    public string Language { get; set; } = "tr";

    /// <summary>How far (in pixels) the mouse must move before a gesture counts as a direction instead of a click.</summary>
    public int GestureThreshold { get; set; } = 30;

    public List<Profile> Profiles { get; set; } = new();

    public Profile DefaultProfile => Profiles[0];

    /// <summary>The program-specific profile for <paramref name="processName"/>, or the default profile.</summary>
    public Profile ResolveProfile(string? processName)
    {
        for (int i = 1; i < Profiles.Count; i++)
        {
            if (Profiles[i].Matches(processName))
                return Profiles[i];
        }
        return DefaultProfile;
    }

    /// <summary>
    /// The action for a trigger. A program profile only overrides the triggers it defines;
    /// everything else comes from the default profile.
    /// </summary>
    public ActionDefinition? Resolve(string trigger, string? processName)
    {
        var profile = ResolveProfile(processName);
        if (profile.Mappings.TryGetValue(trigger, out var action))
            return action;
        if (!ReferenceEquals(profile, DefaultProfile) && DefaultProfile.Mappings.TryGetValue(trigger, out action))
            return action;
        return null;
    }

    /// <summary>Virtual-key codes used as triggers in any profile (decides whether a keyboard hook is needed).</summary>
    public IReadOnlyCollection<int> KeyTriggers()
    {
        var keys = new HashSet<int>();
        foreach (var profile in Profiles)
        {
            foreach (var trigger in profile.Mappings.Keys)
            {
                if (Triggers.TryGetKey(trigger, out var vk))
                    keys.Add(vk);
            }
        }
        return keys;
    }

    public static AppConfig CreateDefault() => new()
    {
        Profiles = { new Profile { Name = "Default" } },
    };

    /// <summary>Repairs a config loaded from disk so the rest of the program can rely on its shape.</summary>
    public void Normalize()
    {
        Profiles ??= new List<Profile>();
        Profiles.RemoveAll(p => p == null);
        if (Profiles.Count == 0)
            Profiles.Add(new Profile { Name = "Default" });

        DefaultProfile.Processes.Clear();
        if (string.IsNullOrWhiteSpace(DefaultProfile.Name))
            DefaultProfile.Name = "Default";

        foreach (var profile in Profiles)
        {
            profile.Name ??= "";
            profile.Processes = (profile.Processes ?? new List<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(NormalizeProcessName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var mappings = new Dictionary<string, ActionDefinition>(StringComparer.OrdinalIgnoreCase);
            if (profile.Mappings != null)
            {
                foreach (var (trigger, action) in profile.Mappings)
                {
                    if (action != null && Triggers.IsValid(trigger))
                        mappings[trigger] = action;
                }
            }
            profile.Mappings = mappings;
        }

        GestureThreshold = Math.Clamp(GestureThreshold, MinGestureThreshold, MaxGestureThreshold);
        if (Language is not ("tr" or "en"))
            Language = "tr";
        Version = CurrentVersion;
    }

    /// <summary>"C:\Apps\Chrome.EXE" or "chrome" → "chrome.exe".</summary>
    public static string NormalizeProcessName(string name)
    {
        var trimmed = name.Trim().Trim('"');
        var slash = trimmed.LastIndexOfAny(new[] { '\\', '/' });
        if (slash >= 0)
            trimmed = trimmed[(slash + 1)..];
        trimmed = trimmed.ToLowerInvariant();
        if (trimmed.Length > 0 && !trimmed.EndsWith(".exe", StringComparison.Ordinal))
            trimmed += ".exe";
        return trimmed;
    }
}
