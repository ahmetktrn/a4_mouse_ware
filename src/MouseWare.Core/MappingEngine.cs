namespace MouseWare.Core;

public enum InvocationPhase
{
    /// <summary>Do the whole action once (press and release).</summary>
    Tap,
    /// <summary>Start a held action (button/key down).</summary>
    Press,
    /// <summary>End a held action (button/key up).</summary>
    Release,
}

public readonly record struct ActionInvocation(ActionDefinition Action, InvocationPhase Phase, string Trigger);

/// <summary>What the input hook should do with one physical event.</summary>
public readonly record struct EngineDecision(bool Suppress, IReadOnlyList<ActionInvocation> Actions)
{
    public static readonly EngineDecision Pass = new(false, Array.Empty<ActionInvocation>());
    public static readonly EngineDecision Block = new(true, Array.Empty<ActionInvocation>());

    public static EngineDecision Run(ActionInvocation invocation) => new(true, new[] { invocation });
}

public static class GestureMath
{
    /// <summary>
    /// Classifies a mouse movement (screen coordinates, y grows downwards) into one of 8
    /// directions, or <see cref="GestureDirection.Click"/> when it is shorter than the threshold.
    /// </summary>
    public static GestureDirection Classify(int dx, int dy, int threshold)
    {
        if ((long)dx * dx + (long)dy * dy < (long)threshold * threshold)
            return GestureDirection.Click;

        double angle = Math.Atan2(-dy, dx) * 180.0 / Math.PI; // 0 = right, 90 = up
        if (angle < 0)
            angle += 360;
        int sector = (int)Math.Floor((angle + 22.5) / 45.0) % 8;
        return sector switch
        {
            0 => GestureDirection.Right,
            1 => GestureDirection.UpRight,
            2 => GestureDirection.Up,
            3 => GestureDirection.UpLeft,
            4 => GestureDirection.Left,
            5 => GestureDirection.DownLeft,
            6 => GestureDirection.Down,
            _ => GestureDirection.DownRight,
        };
    }
}

/// <summary>
/// Decides, for each physical button/wheel/key event, whether to let it through and which
/// actions to run. It has no Windows dependencies so it can be unit-tested anywhere.
///
/// Every button-down that is taken over is remembered, and the matching button-up is handled
/// with the same action — even if the config or the program under the cursor changed in
/// between. This keeps presses and releases paired, so no key or button is left "stuck".
/// </summary>
public sealed class MappingEngine
{
    private sealed record HeldState(ActionDefinition Action, int StartX, int StartY);

    private readonly object _lock = new();
    private readonly Dictionary<string, HeldState> _held = new(StringComparer.OrdinalIgnoreCase);
    private AppConfig _config;

    public MappingEngine(AppConfig config)
    {
        _config = config;
    }

    public AppConfig Config
    {
        get { lock (_lock) return _config; }
    }

    /// <summary>Replaces the config. The engine must own this instance (pass a clone).</summary>
    public void UpdateConfig(AppConfig config)
    {
        lock (_lock)
            _config = config;
    }

    public bool Enabled
    {
        get { lock (_lock) return _config.Enabled; }
        set { lock (_lock) _config.Enabled = value; }
    }

    public bool HasHeld
    {
        get { lock (_lock) return _held.Count > 0; }
    }

    public EngineDecision ButtonDown(string trigger, int x, int y, string? processName)
    {
        lock (_lock)
        {
            ActionInvocation? staleRelease = null;
            if (_held.TryGetValue(trigger, out var existing))
            {
                // A key that is held down repeats its key-down: keep swallowing it.
                if (Triggers.IsKey(trigger))
                    return EngineDecision.Block;

                // A mouse button cannot repeat, so we missed its button-up (for example
                // Windows dropped the hook for a moment). Finish the old press first.
                _held.Remove(trigger);
                if (existing.Action.IsHoldable)
                    staleRelease = new ActionInvocation(existing.Action, InvocationPhase.Release, trigger);
            }

            var decision = Down(trigger, x, y, processName);
            if (staleRelease == null)
                return decision;

            var actions = new List<ActionInvocation> { staleRelease.Value };
            actions.AddRange(decision.Actions);
            return new EngineDecision(decision.Suppress, actions);
        }
    }

    private EngineDecision Down(string trigger, int x, int y, string? processName)
    {
        if (!_config.Enabled)
            return EngineDecision.Pass;

        var action = _config.Resolve(trigger, processName);
        if (action == null || action.Type == ActionType.Default)
            return EngineDecision.Pass;

        _held[trigger] = new HeldState(action, x, y);

        return action.Type switch
        {
            ActionType.Disabled or ActionType.Gesture => EngineDecision.Block,
            _ when action.IsHoldable => EngineDecision.Run(new ActionInvocation(action, InvocationPhase.Press, trigger)),
            _ => EngineDecision.Run(new ActionInvocation(action, InvocationPhase.Tap, trigger)),
        };
    }

    public EngineDecision ButtonUp(string trigger, int x, int y)
    {
        lock (_lock)
        {
            if (!_held.Remove(trigger, out var state))
                return EngineDecision.Pass;

            var action = state.Action;
            if (action.Type == ActionType.Gesture)
                return FinishGesture(trigger, action, x - state.StartX, y - state.StartY);

            if (action.IsHoldable)
                return EngineDecision.Run(new ActionInvocation(action, InvocationPhase.Release, trigger));

            return EngineDecision.Block;
        }
    }

    public EngineDecision Wheel(string trigger, string? processName)
    {
        lock (_lock)
        {
            if (!_config.Enabled)
                return EngineDecision.Pass;

            var action = _config.Resolve(trigger, processName);
            if (action == null || action.Type == ActionType.Default)
                return EngineDecision.Pass;

            if (action.Type is ActionType.Disabled or ActionType.Gesture)
                return EngineDecision.Block;

            return EngineDecision.Run(new ActionInvocation(action, InvocationPhase.Tap, trigger));
        }
    }

    /// <summary>Forgets held buttons (e.g. after the hook was reinstalled). Returns releases for held keys/buttons.</summary>
    public IReadOnlyList<ActionInvocation> ReleaseAll()
    {
        lock (_lock)
        {
            var releases = _held
                .Where(kv => kv.Value.Action.IsHoldable)
                .Select(kv => new ActionInvocation(kv.Value.Action, InvocationPhase.Release, kv.Key))
                .ToList();
            _held.Clear();
            return releases;
        }
    }

    private EngineDecision FinishGesture(string trigger, ActionDefinition gesture, int dx, int dy)
    {
        var direction = GestureMath.Classify(dx, dy, _config.GestureThreshold);
        ActionDefinition? target = null;
        gesture.Gestures?.TryGetValue(direction, out target);

        bool isPlainClick = direction == GestureDirection.Click
            && (target == null || target.Type == ActionType.Default);
        if (isPlainClick)
        {
            // No click action set: behave like the original button so it still works normally.
            var button = Triggers.ToButton(trigger);
            if (button == null)
                return EngineDecision.Block;
            target = ActionDefinition.Mouse(button.Value);
        }

        if (target == null || target.Type is ActionType.Default or ActionType.Disabled or ActionType.Gesture)
            return EngineDecision.Block;

        return EngineDecision.Run(new ActionInvocation(target, InvocationPhase.Tap, trigger));
    }
}
