namespace CaptureDoc.Core.Platform;

[Flags]
public enum HotkeyModifiers { None = 0, Alt = 1, Control = 2, Shift = 4, Win = 8 }

/// <summary>Parses "Ctrl+Shift+4", "Alt+PrintScreen", "F9".</summary>
public sealed record HotkeyGesture(HotkeyModifiers Modifiers, string Key)
{
    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = new HotkeyGesture(HotkeyModifiers.None, "");
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var mods = HotkeyModifiers.None;
        string? key = null;
        foreach (var p in parts)
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl" or "strg" or "control": mods |= HotkeyModifiers.Control; break;
                case "alt": mods |= HotkeyModifiers.Alt; break;
                case "shift" or "umschalt": mods |= HotkeyModifiers.Shift; break;
                case "win" or "cmd" or "meta" or "super": mods |= HotkeyModifiers.Win; break;
                default:
                    if (key is not null) return false;
                    key = Normalize(p);
                    break;
            }
        }
        if (key is null) return false;
        gesture = new HotkeyGesture(mods, key);
        return true;
    }

    private static string Normalize(string k) => k.ToLowerInvariant() switch
    {
        "print" or "prtsc" or "printscreen" or "druck" => "PrintScreen",
        "esc" or "escape" => "Escape",
        "space" or "leertaste" => "Space",
        _ => k.Length == 1 ? k.ToUpperInvariant() : char.ToUpperInvariant(k[0]) + k[1..],
    };

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(Key);
        return string.Join('+', parts);
    }
}
