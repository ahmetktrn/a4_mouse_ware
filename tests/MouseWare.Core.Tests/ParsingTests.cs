using MouseWare.Core;

namespace MouseWare.Core.Tests;

public class KeyComboTests
{
    [Theory]
    [InlineData("Ctrl+C", "Ctrl+C")]
    [InlineData("shift+ctrl+t", "Ctrl+Shift+T")]
    [InlineData("Win+Ctrl+Right", "Ctrl+Win+Right")]
    [InlineData("Control+Escape", "Ctrl+Esc")]
    [InlineData("Ctrl", "Ctrl")]
    [InlineData("Ctrl+Shift", "Ctrl+Shift")]
    [InlineData("Ctrl++", "Ctrl+Plus")]
    [InlineData("MediaPlayPause", "MediaPlayPause")]
    [InlineData("F13", "F13")]
    [InlineData("LCtrl+A", "Ctrl+A")]
    [InlineData("VK0x7C", "F13")]
    [InlineData("VK255", "VK255")]
    [InlineData(" Alt + F4 ", "Alt+F4")]
    public void Parses_and_formats(string input, string expected)
    {
        Assert.True(KeyCombo.TryParse(input, out var combo));
        Assert.Equal(expected, combo.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Nope")]
    [InlineData("A+B")]
    [InlineData("A+Ctrl")]
    [InlineData("Ctrl+Ctrl")]
    [InlineData("VK0")]
    [InlineData("VK999")]
    public void Rejects_invalid_input(string input)
    {
        Assert.False(KeyCombo.TryParse(input, out _));
    }

    [Fact]
    public void Parsed_codes_match_windows_virtual_keys()
    {
        Assert.True(KeyCombo.TryParse("Ctrl+Alt+Shift+Win+A", out var combo));
        Assert.Equal(new[] { 0x11, 0x12, 0x10, 0x5B }, combo.Modifiers);
        Assert.Equal(0x41, combo.Key);
    }

    [Fact]
    public void FromPressed_drops_duplicate_and_self_modifiers()
    {
        var combo = KeyCombo.FromPressed(new[] { 0xA3, 0x11, 0xA0 }, 0x54);
        Assert.Equal("Ctrl+Shift+T", combo.ToString());

        var modifierOnly = KeyCombo.FromPressed(new[] { 0xA2 }, 0xA2);
        Assert.Equal("Ctrl", modifierOnly.ToString());
    }

    [Fact]
    public void Every_canonical_name_round_trips()
    {
        foreach (var name in KeyNames.AllNames)
        {
            Assert.True(KeyNames.TryGetVk(name, out var vk), name);
            Assert.Equal(name, KeyNames.Name(vk));
        }
    }

    [Fact]
    public void Extended_keys_are_flagged()
    {
        Assert.True(KeyNames.IsExtended(0x25)); // Left arrow
        Assert.True(KeyNames.IsExtended(0xB3)); // Play/pause
        Assert.True(KeyNames.IsExtended(0x5B)); // Win
        Assert.False(KeyNames.IsExtended(0x41)); // A
        Assert.False(KeyNames.IsExtended(0x11)); // Ctrl
    }
}

public class MacroParserTests
{
    [Fact]
    public void Parses_all_commands_in_english_and_turkish()
    {
        var result = MacroParser.Parse("""
            # comment
            key Ctrl+C
            tuş Alt+Tab
            down Shift
            bırak Shift
            wait 150
            yaz Merhaba dünya
            text  two spaces
            click right 2
            tıkla orta
            scroll down 3
            kaydır sağ
            """);

        Assert.True(result.Success, string.Join(", ", result.Errors));
        Assert.Collection(result.Steps,
            s => Assert.Equal("Ctrl+C", Assert.IsType<KeyTapStep>(s).Combo.ToString()),
            s => Assert.Equal("Alt+Tab", Assert.IsType<KeyTapStep>(s).Combo.ToString()),
            s => Assert.Equal("Shift", Assert.IsType<KeyDownStep>(s).Combo.ToString()),
            s => Assert.Equal("Shift", Assert.IsType<KeyUpStep>(s).Combo.ToString()),
            s => Assert.Equal(150, Assert.IsType<DelayStep>(s).Milliseconds),
            s => Assert.Equal("Merhaba dünya", Assert.IsType<TextStep>(s).Text),
            s => Assert.Equal(" two spaces", Assert.IsType<TextStep>(s).Text),
            s => Assert.Equal(new ClickStep(MouseButtonKind.Right, 2), s),
            s => Assert.Equal(new ClickStep(MouseButtonKind.Middle, 1), s),
            s => Assert.Equal(new ScrollStep(ScrollDirection.Down, 3), s),
            s => Assert.Equal(new ScrollStep(ScrollDirection.Right, 1), s));
    }

    [Fact]
    public void Click_defaults_to_one_left_click()
    {
        var result = MacroParser.Parse("click");
        Assert.Equal(new ClickStep(MouseButtonKind.Left, 1), Assert.Single(result.Steps));
    }

    [Fact]
    public void Reports_errors_with_line_numbers()
    {
        var result = MacroParser.Parse("key Ctrl+C\njump 5\nwait abc\nkey Nope\nwait\nclick sideways\nscroll diagonal\nwait 999999\nclick left 0");

        Assert.Single(result.Steps);
        Assert.Collection(result.Errors,
            e => Assert.Equal(new MacroError(2, MacroErrorKind.UnknownCommand, "jump"), e),
            e => Assert.Equal(new MacroError(3, MacroErrorKind.InvalidNumber, "abc"), e),
            e => Assert.Equal(new MacroError(4, MacroErrorKind.InvalidKey, "Nope"), e),
            e => Assert.Equal(new MacroError(5, MacroErrorKind.MissingArgument, "wait"), e),
            e => Assert.Equal(new MacroError(6, MacroErrorKind.InvalidButton, "sideways"), e),
            e => Assert.Equal(new MacroError(7, MacroErrorKind.InvalidDirection, "diagonal"), e),
            e => Assert.Equal(new MacroError(8, MacroErrorKind.InvalidNumber, "999999"), e),
            e => Assert.Equal(new MacroError(9, MacroErrorKind.InvalidNumber, "0"), e));
    }

    [Fact]
    public void Empty_macro_is_valid()
    {
        Assert.True(MacroParser.Parse("").Success);
        Assert.True(MacroParser.Parse(null).Success);
    }
}

public class ConfigTests
{
    [Fact]
    public void Round_trips_through_json()
    {
        var config = AppConfig.CreateDefault();
        config.Language = "en";
        config.GestureThreshold = 45;
        config.DefaultProfile.Mappings[Triggers.X1] = ActionDefinition.Key("Ctrl+C", "copy");
        config.DefaultProfile.Mappings[Triggers.Right] = new ActionDefinition
        {
            Type = ActionType.Gesture,
            Gestures = new()
            {
                [GestureDirection.Up] = ActionDefinition.WindowAction(WindowCommand.MaximizeRestore),
                [GestureDirection.DownLeft] = new ActionDefinition { Type = ActionType.Run, Path = @"C:\Windows\notepad.exe", Arguments = "a.txt" },
            },
        };
        config.Profiles.Add(new Profile { Name = "Chrome", Processes = { "chrome.exe" } });

        var json = ConfigStore.Serialize(config);
        var copy = ConfigStore.Deserialize(json);

        Assert.Contains("\"Gesture\"", json);
        Assert.Contains("\"DownLeft\"", json);
        Assert.Equal(json, ConfigStore.Serialize(copy));
        Assert.Equal("Ctrl+C", copy.DefaultProfile.Mappings["x1"].Keys);
        Assert.Equal(@"C:\Windows\notepad.exe", copy.DefaultProfile.Mappings[Triggers.Right].Gestures![GestureDirection.DownLeft].Path);
    }

    [Fact]
    public void Normalize_repairs_bad_values()
    {
        var config = ConfigStore.Deserialize("""
            {
              "language": "de",
              "gestureThreshold": 5000,
              "profiles": [
                { "name": "", "processes": ["x.exe"], "mappings": { "Left": { "type": "Disabled" }, "Bogus": { "type": "Disabled" } } },
                { "name": "App", "processes": ["C:\\Apps\\Foo.EXE", "bar", "", "foo.exe"] }
              ]
            }
            """);

        Assert.Equal("tr", config.Language);
        Assert.Equal(AppConfig.MaxGestureThreshold, config.GestureThreshold);
        Assert.Equal("Default", config.DefaultProfile.Name);
        Assert.Empty(config.DefaultProfile.Processes);
        Assert.True(config.DefaultProfile.Mappings.ContainsKey("left"));
        Assert.False(config.DefaultProfile.Mappings.ContainsKey("Bogus"));
        Assert.Equal(new[] { "foo.exe", "bar.exe" }, config.Profiles[1].Processes);
    }

    [Fact]
    public void Empty_profile_list_gets_a_default_profile()
    {
        var config = ConfigStore.Deserialize("""{ "profiles": [] }""");
        Assert.Single(config.Profiles);
    }

    [Fact]
    public void Load_keeps_a_copy_of_a_broken_file()
    {
        var dir = Directory.CreateTempSubdirectory("mousewaretest");
        try
        {
            var path = Path.Combine(dir.FullName, "config.json");
            File.WriteAllText(path, "{ not json");

            var result = ConfigStore.Load(path);

            Assert.NotNull(result.Error);
            Assert.Single(result.Config.Profiles);
            Assert.True(File.Exists(Path.Combine(dir.FullName, "config.bad.json")));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Save_then_load_gives_same_config()
    {
        var dir = Directory.CreateTempSubdirectory("mousewaretest");
        try
        {
            var path = Path.Combine(dir.FullName, "sub", "config.json");
            var config = AppConfig.CreateDefault();
            config.DefaultProfile.Mappings[Triggers.ForKey(0x7C)] = ActionDefinition.Key("Ctrl+V");

            ConfigStore.Save(path, config);
            var loaded = ConfigStore.Load(path);

            Assert.Null(loaded.Error);
            Assert.Equal(new[] { 0x7C }, loaded.Config.KeyTriggers());
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("chrome.exe", "chrome.exe")]
    [InlineData("Chrome", "chrome.exe")]
    [InlineData(@"C:\Program Files\App\APP.EXE", "app.exe")]
    [InlineData("\"notepad.exe\"", "notepad.exe")]
    public void Process_names_are_normalized(string input, string expected)
    {
        Assert.Equal(expected, AppConfig.NormalizeProcessName(input));
    }

    [Fact]
    public void Clone_is_deep()
    {
        var action = new ActionDefinition
        {
            Type = ActionType.Gesture,
            Gestures = new() { [GestureDirection.Up] = ActionDefinition.Key("A") },
        };
        var copy = action.Clone();
        copy.Gestures![GestureDirection.Up].Keys = "B";

        Assert.Equal("A", action.Gestures[GestureDirection.Up].Keys);
    }

    [Fact]
    public void Preset_ids_are_unique_and_keys_valid()
    {
        Assert.Equal(Presets.All.Count, Presets.All.Select(p => p.Id).Distinct().Count());
        foreach (var preset in Presets.All.Where(p => p.Action.Type == ActionType.KeyCombo))
            Assert.True(KeyCombo.TryParse(preset.Action.Keys, out _), preset.Id);
    }
}
