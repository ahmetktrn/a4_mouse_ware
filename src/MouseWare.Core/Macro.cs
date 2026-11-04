using System.Globalization;

namespace MouseWare.Core;

public abstract record MacroStep;

/// <summary>Press and release a shortcut.</summary>
public sealed record KeyTapStep(KeyCombo Combo) : MacroStep;

/// <summary>Press a shortcut and keep it held.</summary>
public sealed record KeyDownStep(KeyCombo Combo) : MacroStep;

/// <summary>Release a shortcut held with <see cref="KeyDownStep"/>.</summary>
public sealed record KeyUpStep(KeyCombo Combo) : MacroStep;

public sealed record DelayStep(int Milliseconds) : MacroStep;

public sealed record TextStep(string Text) : MacroStep;

public sealed record ClickStep(MouseButtonKind Button, int Count) : MacroStep;

public sealed record ScrollStep(ScrollDirection Direction, int Amount) : MacroStep;

public enum MacroErrorKind
{
    UnknownCommand,
    MissingArgument,
    InvalidKey,
    InvalidNumber,
    InvalidButton,
    InvalidDirection,
}

public sealed record MacroError(int Line, MacroErrorKind Kind, string Token);

public sealed record MacroParseResult(IReadOnlyList<MacroStep> Steps, IReadOnlyList<MacroError> Errors)
{
    public bool Success => Errors.Count == 0;
}

/// <summary>
/// Parses the macro language: one command per line, in English or Turkish.
/// <code>
/// key Ctrl+C        (tuş)    press and release
/// down Shift        (bas)    hold
/// up Shift          (bırak)  release
/// wait 200          (bekle)  milliseconds
/// text Hello        (yaz)    type text
/// click left 2      (tıkla)  left/right/middle/x1/x2 (sol/sağ/orta), optional count
/// scroll down 3     (kaydır) up/down/left/right (yukarı/aşağı/sol/sağ), optional amount
/// # comment
/// </code>
/// </summary>
public static class MacroParser
{
    public const int MaxDelayMs = 60_000;
    public const int MaxRepeat = 50;

    public static MacroParseResult Parse(string? source)
    {
        var steps = new List<MacroStep>();
        var errors = new List<MacroError>();
        if (string.IsNullOrEmpty(source))
            return new MacroParseResult(steps, errors);

        var lines = source.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            int lineNo = i + 1;
            var line = lines[i].TrimStart();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("//", StringComparison.Ordinal))
                continue;

            int space = line.IndexOfAny(new[] { ' ', '\t' });
            var command = (space < 0 ? line : line[..space]).ToLowerInvariant();
            var rest = space < 0 ? "" : line[(space + 1)..];
            var argument = rest.Trim();

            switch (command)
            {
                case "key" or "tus" or "tuş" or "down" or "bas" or "up" or "birak" or "bırak":
                    if (argument.Length == 0)
                    {
                        errors.Add(new MacroError(lineNo, MacroErrorKind.MissingArgument, command));
                    }
                    else if (!KeyCombo.TryParse(argument, out var combo))
                    {
                        errors.Add(new MacroError(lineNo, MacroErrorKind.InvalidKey, argument));
                    }
                    else
                    {
                        steps.Add(command switch
                        {
                            "down" or "bas" => new KeyDownStep(combo),
                            "up" or "birak" or "bırak" => new KeyUpStep(combo),
                            _ => new KeyTapStep(combo),
                        });
                    }
                    break;

                case "wait" or "bekle" or "delay":
                    if (argument.Length == 0)
                        errors.Add(new MacroError(lineNo, MacroErrorKind.MissingArgument, command));
                    else if (!int.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms) || ms < 0 || ms > MaxDelayMs)
                        errors.Add(new MacroError(lineNo, MacroErrorKind.InvalidNumber, argument));
                    else
                        steps.Add(new DelayStep(ms));
                    break;

                case "text" or "yaz":
                    // Keep the text exactly as written after the single separating space.
                    if (rest.Length == 0)
                        errors.Add(new MacroError(lineNo, MacroErrorKind.MissingArgument, command));
                    else
                        steps.Add(new TextStep(rest));
                    break;

                case "click" or "tikla" or "tıkla":
                    ParseClick(lineNo, argument, steps, errors);
                    break;

                case "scroll" or "kaydir" or "kaydır":
                    ParseScroll(lineNo, command, argument, steps, errors);
                    break;

                default:
                    errors.Add(new MacroError(lineNo, MacroErrorKind.UnknownCommand, command));
                    break;
            }
        }

        return new MacroParseResult(steps, errors);
    }

    private static void ParseClick(int lineNo, string argument, List<MacroStep> steps, List<MacroError> errors)
    {
        var parts = argument.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var button = MouseButtonKind.Left;
        int count = 1;
        if (parts.Length > 0)
        {
            var parsed = ParseButton(parts[0]);
            if (parsed == null)
            {
                errors.Add(new MacroError(lineNo, MacroErrorKind.InvalidButton, parts[0]));
                return;
            }
            button = parsed.Value;
        }
        if (parts.Length > 1 && !TryParseCount(parts[1], out count))
        {
            errors.Add(new MacroError(lineNo, MacroErrorKind.InvalidNumber, parts[1]));
            return;
        }
        steps.Add(new ClickStep(button, count));
    }

    private static void ParseScroll(int lineNo, string command, string argument, List<MacroStep> steps, List<MacroError> errors)
    {
        var parts = argument.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            errors.Add(new MacroError(lineNo, MacroErrorKind.MissingArgument, command));
            return;
        }
        var direction = ParseDirection(parts[0]);
        if (direction == null)
        {
            errors.Add(new MacroError(lineNo, MacroErrorKind.InvalidDirection, parts[0]));
            return;
        }
        int amount = 1;
        if (parts.Length > 1 && !TryParseCount(parts[1], out amount))
        {
            errors.Add(new MacroError(lineNo, MacroErrorKind.InvalidNumber, parts[1]));
            return;
        }
        steps.Add(new ScrollStep(direction.Value, amount));
    }

    private static bool TryParseCount(string text, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 1 && value <= MaxRepeat;

    private static MouseButtonKind? ParseButton(string text) => text.ToLowerInvariant() switch
    {
        "left" or "sol" => MouseButtonKind.Left,
        "right" or "sag" or "sağ" => MouseButtonKind.Right,
        "middle" or "orta" => MouseButtonKind.Middle,
        "x1" or "back" or "geri" => MouseButtonKind.X1,
        "x2" or "forward" or "ileri" => MouseButtonKind.X2,
        _ => null,
    };

    private static ScrollDirection? ParseDirection(string text) => text.ToLowerInvariant() switch
    {
        "up" or "yukari" or "yukarı" => ScrollDirection.Up,
        "down" or "asagi" or "aşağı" => ScrollDirection.Down,
        "left" or "sol" => ScrollDirection.Left,
        "right" or "sag" or "sağ" => ScrollDirection.Right,
        _ => null,
    };
}
