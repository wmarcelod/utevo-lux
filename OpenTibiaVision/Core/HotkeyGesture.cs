using System;
using System.Text;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace OpenTibiaVision.Core;

/// <summary>
/// A key + modifier combination. Serializable (for rebinds stored in settings) and comparable
/// so the conflict registry can key on it.
/// </summary>
public readonly record struct HotkeyGesture(Key Key, ModifierKeys Modifiers)
{
    [JsonIgnore]
    public bool IsEmpty => Key == Key.None;

    public static readonly HotkeyGesture None = new(Key.None, ModifierKeys.None);

    public override string ToString()
    {
        if (IsEmpty)
            return "(nenhum)";

        var sb = new StringBuilder();
        if (Modifiers.HasFlag(ModifierKeys.Control)) sb.Append("Ctrl+");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) sb.Append("Alt+");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) sb.Append("Shift+");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) sb.Append("Win+");
        sb.Append(Key);
        return sb.ToString();
    }
}
