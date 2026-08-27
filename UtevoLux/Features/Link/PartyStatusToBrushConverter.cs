using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using UtevoLux.UI;

namespace UtevoLux.Features.Link;

/// <summary>
/// Maps a <see cref="PartyMemberStatus"/> to the coloured status dot brush, reading the fork theme
/// tokens (Success / Warning / Danger) with hardcoded fallbacks so it renders even before the
/// merged dictionaries are realized. Used by the page member list; the click-through overlay builds
/// its dots in code from the same colours.
/// </summary>
public sealed class PartyStatusToBrushConverter : IValueConverter
{
    public static Brush BrushFor(PartyMemberStatus status) => status switch
    {
        PartyMemberStatus.Connected => ThemeAccess.Brush("SuccessBrush", "#FF3FB950"),
        PartyMemberStatus.Lagging => ThemeAccess.Brush("WarningBrush", "#FFD8A24A"),
        PartyMemberStatus.Disconnected => ThemeAccess.Brush("DangerBrush", "#FFE5534B"),
        _ => ThemeAccess.Brush("TextMutedBrush", "#FF69707D"),
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BrushFor(value is PartyMemberStatus s ? s : PartyMemberStatus.Connected);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
