using MouseWare.Core;

namespace MouseWare.Core.Tests;

public class MappingEngineTests
{
    private static AppConfig ConfigWith(string trigger, ActionDefinition action)
    {
        var config = AppConfig.CreateDefault();
        config.DefaultProfile.Mappings[trigger] = action;
        return config;
    }

    [Fact]
    public void Unmapped_buttons_pass_through()
    {
        var engine = new MappingEngine(AppConfig.CreateDefault());

        Assert.Equal(EngineDecision.Pass, engine.ButtonDown(Triggers.Left, 0, 0, null));
        Assert.Equal(EngineDecision.Pass, engine.ButtonUp(Triggers.Left, 0, 0));
        Assert.Equal(EngineDecision.Pass, engine.Wheel(Triggers.WheelUp, null));
    }

    [Fact]
    public void Default_action_passes_through()
    {
        var engine = new MappingEngine(ConfigWith(Triggers.X1, ActionDefinition.OfType(ActionType.Default)));

        Assert.False(engine.ButtonDown(Triggers.X1, 0, 0, null).Suppress);
        Assert.False(engine.ButtonUp(Triggers.X1, 0, 0).Suppress);
    }

    [Fact]
    public void Disabled_swallows_down_and_up_without_actions()
    {
        var engine = new MappingEngine(ConfigWith(Triggers.Middle, ActionDefinition.OfType(ActionType.Disabled)));

        var down = engine.ButtonDown(Triggers.Middle, 0, 0, null);
        var up = engine.ButtonUp(Triggers.Middle, 0, 0);

        Assert.True(down.Suppress);
        Assert.Empty(down.Actions);
        Assert.True(up.Suppress);
        Assert.Empty(up.Actions);
    }

    [Fact]
    public void Key_combo_is_pressed_on_down_and_released_on_up()
    {
        var action = ActionDefinition.Key("Ctrl+C");
        var engine = new MappingEngine(ConfigWith(Triggers.X1, action));

        var down = engine.ButtonDown(Triggers.X1, 0, 0, null);
        var up = engine.ButtonUp(Triggers.X1, 0, 0);

        Assert.True(down.Suppress);
        Assert.Equal(InvocationPhase.Press, Assert.Single(down.Actions).Phase);
        Assert.True(up.Suppress);
        Assert.Equal(InvocationPhase.Release, Assert.Single(up.Actions).Phase);
        Assert.Same(action, up.Actions[0].Action);
    }

    [Fact]
    public void One_shot_actions_run_once_on_down()
    {
        var engine = new MappingEngine(ConfigWith(Triggers.X2, new ActionDefinition { Type = ActionType.OpenUrl, Url = "example.com" }));

        var down = engine.ButtonDown(Triggers.X2, 0, 0, null);
        var up = engine.ButtonUp(Triggers.X2, 0, 0);

        Assert.Equal(InvocationPhase.Tap, Assert.Single(down.Actions).Phase);
        Assert.True(up.Suppress);
        Assert.Empty(up.Actions);
    }

    [Fact]
    public void Up_uses_the_action_from_the_down_even_if_config_changed()
    {
        var action = ActionDefinition.Key("Shift");
        var engine = new MappingEngine(ConfigWith(Triggers.X1, action));

        engine.ButtonDown(Triggers.X1, 0, 0, null);
        engine.UpdateConfig(AppConfig.CreateDefault());
        var up = engine.ButtonUp(Triggers.X1, 0, 0);

        Assert.True(up.Suppress);
        Assert.Same(action, Assert.Single(up.Actions).Action);
        Assert.Equal(InvocationPhase.Release, up.Actions[0].Phase);
    }

    [Fact]
    public void Up_uses_the_action_from_the_down_even_if_disabled_meanwhile()
    {
        var engine = new MappingEngine(ConfigWith(Triggers.X1, ActionDefinition.Key("Shift")));

        engine.ButtonDown(Triggers.X1, 0, 0, null);
        engine.Enabled = false;
        var up = engine.ButtonUp(Triggers.X1, 0, 0);

        Assert.Equal(InvocationPhase.Release, Assert.Single(up.Actions).Phase);
    }

    [Fact]
    public void Repeated_down_while_held_is_swallowed_without_actions()
    {
        var trigger = Triggers.ForKey(0x7C);
        var engine = new MappingEngine(ConfigWith(trigger, ActionDefinition.Key("Ctrl+V")));

        engine.ButtonDown(trigger, 0, 0, null);
        var repeat = engine.ButtonDown(trigger, 0, 0, null);

        Assert.True(repeat.Suppress);
        Assert.Empty(repeat.Actions);
    }

    [Fact]
    public void Missed_mouse_button_up_does_not_leave_the_button_stuck()
    {
        var engine = new MappingEngine(ConfigWith(Triggers.X1, ActionDefinition.Key("Shift")));

        engine.ButtonDown(Triggers.X1, 0, 0, null);
        // The up event was lost; the next press must release the old one and work again.
        var second = engine.ButtonDown(Triggers.X1, 0, 0, null);

        Assert.True(second.Suppress);
        Assert.Collection(second.Actions,
            a => Assert.Equal(InvocationPhase.Release, a.Phase),
            a => Assert.Equal(InvocationPhase.Press, a.Phase));
        Assert.Equal(InvocationPhase.Release, Assert.Single(engine.ButtonUp(Triggers.X1, 0, 0).Actions).Phase);
    }

    [Fact]
    public void Missed_up_while_disabled_lets_the_button_work_normally_again()
    {
        var engine = new MappingEngine(ConfigWith(Triggers.Middle, ActionDefinition.OfType(ActionType.Disabled)));

        engine.ButtonDown(Triggers.Middle, 0, 0, null);
        engine.Enabled = false;
        var again = engine.ButtonDown(Triggers.Middle, 0, 0, null);

        Assert.False(again.Suppress);
    }

    [Fact]
    public void Disabled_engine_passes_everything()
    {
        var config = ConfigWith(Triggers.Right, ActionDefinition.OfType(ActionType.Disabled));
        config.Enabled = false;
        var engine = new MappingEngine(config);

        Assert.False(engine.ButtonDown(Triggers.Right, 0, 0, null).Suppress);
        Assert.False(engine.ButtonUp(Triggers.Right, 0, 0).Suppress);
    }

    [Fact]
    public void Wheel_runs_tap_and_blocks()
    {
        var engine = new MappingEngine(ConfigWith(Triggers.WheelDown, ActionDefinition.Key("VolumeDown")));

        var decision = engine.Wheel(Triggers.WheelDown, null);

        Assert.True(decision.Suppress);
        Assert.Equal(InvocationPhase.Tap, Assert.Single(decision.Actions).Phase);
    }

    [Fact]
    public void App_profile_overrides_only_its_own_triggers()
    {
        var config = AppConfig.CreateDefault();
        config.DefaultProfile.Mappings[Triggers.X1] = ActionDefinition.Key("Ctrl+C");
        config.DefaultProfile.Mappings[Triggers.X2] = ActionDefinition.Key("Ctrl+V");
        config.Profiles.Add(new Profile
        {
            Name = "Chrome",
            Processes = { "chrome.exe" },
            Mappings = { [Triggers.X1] = ActionDefinition.Key("Ctrl+Tab") },
        });
        var engine = new MappingEngine(config);

        var x1 = engine.ButtonDown(Triggers.X1, 0, 0, "CHROME.EXE");
        var x2 = engine.ButtonDown(Triggers.X2, 0, 0, "chrome.exe");
        engine.ButtonUp(Triggers.X1, 0, 0);
        engine.ButtonUp(Triggers.X2, 0, 0);
        var x1Elsewhere = engine.ButtonDown(Triggers.X1, 0, 0, "notepad.exe");

        Assert.Equal("Ctrl+Tab", x1.Actions[0].Action.Keys);
        Assert.Equal("Ctrl+V", x2.Actions[0].Action.Keys);
        Assert.Equal("Ctrl+C", x1Elsewhere.Actions[0].Action.Keys);
    }

    [Fact]
    public void App_profile_can_restore_default_behaviour()
    {
        var config = AppConfig.CreateDefault();
        config.DefaultProfile.Mappings[Triggers.Middle] = ActionDefinition.OfType(ActionType.Disabled);
        config.Profiles.Add(new Profile
        {
            Name = "Game",
            Processes = { "game.exe" },
            Mappings = { [Triggers.Middle] = ActionDefinition.OfType(ActionType.Default) },
        });
        var engine = new MappingEngine(config);

        Assert.False(engine.ButtonDown(Triggers.Middle, 0, 0, "game.exe").Suppress);
    }

    [Fact]
    public void Gesture_without_movement_clicks_the_original_button()
    {
        var gesture = new ActionDefinition
        {
            Type = ActionType.Gesture,
            Gestures = new() { [GestureDirection.Up] = ActionDefinition.Key("Win+Tab") },
        };
        var engine = new MappingEngine(ConfigWith(Triggers.Right, gesture));

        var down = engine.ButtonDown(Triggers.Right, 100, 100, null);
        var up = engine.ButtonUp(Triggers.Right, 105, 103);

        Assert.True(down.Suppress);
        Assert.Empty(down.Actions);
        var click = Assert.Single(up.Actions);
        Assert.Equal(ActionType.MouseButton, click.Action.Type);
        Assert.Equal(MouseButtonKind.Right, click.Action.Button);
        Assert.Equal(InvocationPhase.Tap, click.Phase);
    }

    [Fact]
    public void Gesture_direction_runs_its_action()
    {
        var gesture = new ActionDefinition
        {
            Type = ActionType.Gesture,
            Gestures = new()
            {
                [GestureDirection.Up] = ActionDefinition.Key("Win+Tab"),
                [GestureDirection.Left] = ActionDefinition.Key("Alt+Left"),
            },
        };
        var engine = new MappingEngine(ConfigWith(Triggers.Right, gesture));

        engine.ButtonDown(Triggers.Right, 500, 500, null);
        var up = engine.ButtonUp(Triggers.Right, 500, 400);
        engine.ButtonDown(Triggers.Right, 500, 500, null);
        var left = engine.ButtonUp(Triggers.Right, 380, 510);
        engine.ButtonDown(Triggers.Right, 500, 500, null);
        var unassigned = engine.ButtonUp(Triggers.Right, 600, 600);

        Assert.Equal("Win+Tab", Assert.Single(up.Actions).Action.Keys);
        Assert.Equal("Alt+Left", Assert.Single(left.Actions).Action.Keys);
        Assert.True(unassigned.Suppress);
        Assert.Empty(unassigned.Actions);
    }

    [Fact]
    public void Gesture_click_action_can_be_replaced()
    {
        var gesture = new ActionDefinition
        {
            Type = ActionType.Gesture,
            Gestures = new() { [GestureDirection.Click] = ActionDefinition.Key("Enter") },
        };
        var engine = new MappingEngine(ConfigWith(Triggers.Middle, gesture));

        engine.ButtonDown(Triggers.Middle, 0, 0, null);
        var up = engine.ButtonUp(Triggers.Middle, 0, 0);

        Assert.Equal("Enter", Assert.Single(up.Actions).Action.Keys);
    }

    [Fact]
    public void ReleaseAll_releases_held_keys()
    {
        var engine = new MappingEngine(ConfigWith(Triggers.X1, ActionDefinition.Key("Shift")));
        engine.ButtonDown(Triggers.X1, 0, 0, null);

        var releases = engine.ReleaseAll();

        Assert.Equal(InvocationPhase.Release, Assert.Single(releases).Phase);
        Assert.False(engine.HasHeld);
        Assert.Equal(EngineDecision.Pass, engine.ButtonUp(Triggers.X1, 0, 0));
    }

    [Theory]
    [InlineData(0, 0, GestureDirection.Click)]
    [InlineData(10, -10, GestureDirection.Click)]
    [InlineData(0, -100, GestureDirection.Up)]
    [InlineData(0, 100, GestureDirection.Down)]
    [InlineData(100, 0, GestureDirection.Right)]
    [InlineData(-100, 0, GestureDirection.Left)]
    [InlineData(80, -80, GestureDirection.UpRight)]
    [InlineData(-80, -80, GestureDirection.UpLeft)]
    [InlineData(80, 80, GestureDirection.DownRight)]
    [InlineData(-80, 80, GestureDirection.DownLeft)]
    [InlineData(100, -30, GestureDirection.Right)]
    [InlineData(30, -100, GestureDirection.Up)]
    public void Gesture_directions_are_classified(int dx, int dy, GestureDirection expected)
    {
        Assert.Equal(expected, GestureMath.Classify(dx, dy, 30));
    }
}
