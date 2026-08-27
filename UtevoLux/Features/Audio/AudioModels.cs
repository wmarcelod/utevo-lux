using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using UtevoLux.Core;

namespace UtevoLux.Features.Audio;

/// <summary>
/// How a visual alert banner leaves the screen once shown.
/// </summary>
public enum AlertMode
{
    /// <summary>Fade out automatically after <see cref="AlertConfig.DurationMs"/>.</summary>
    Fade = 0,

    /// <summary>Stay on screen until the user presses the module's dismiss hotkey.</summary>
    StayUntilHotkey = 1
}

/// <summary>Which edge a countdown bar depletes toward (the fill shrinks away from this side).</summary>
public enum BarSide
{
    Left = 0,
    Right = 1,
    Top = 2,
    Bottom = 3
}

/// <summary>
/// A named playable sound. Built-in entries point at a synthesized WAV generated on first use
/// (see <see cref="BeepSynth"/>); user entries point at a file the user picked. Serialized in the
/// shared settings store under the sound-library key.
/// </summary>
public sealed class SoundEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Som";

    /// <summary>Absolute path to a .wav/.mp3 on disk. For built-ins this is filled in lazily.</summary>
    public string FilePath { get; set; } = "";

    /// <summary>True for the synthesized default beep(s); the file is regenerated if missing.</summary>
    public bool BuiltIn { get; set; }

    /// <summary>Only for built-ins: sine frequency (Hz) used to (re)synthesize the WAV.</summary>
    public double BuiltInFrequencyHz { get; set; } = 880.0;

    /// <summary>Only for built-ins: tone length in milliseconds.</summary>
    public int BuiltInDurationMs { get; set; } = 220;
}

/// <summary>
/// Visual-alert overlay configuration for one timer. Geometry is stored in PHYSICAL pixels
/// (principle 8): the banner window is placed with SetWindowPos and read back with
/// GetWindowRect, so it lands exactly on mixed-DPI monitors. A negative position means
/// "auto (top-centre of the primary work area)".
/// </summary>
public sealed class AlertConfig
{
    public bool Enabled { get; set; }

    /// <summary>Banner text; empty means "use the timer name".</summary>
    public string Text { get; set; } = "";

    public AlertMode Mode { get; set; } = AlertMode.Fade;

    /// <summary>Visible time before the fade begins (Fade mode only).</summary>
    public int DurationMs { get; set; } = 2500;

    public string BackgroundHex { get; set; } = "#E6101820";
    public string BorderHex { get; set; } = "#FF4CC2FF";
    public string TextHex { get; set; } = "#FFFFFFFF";
    public double FontSize { get; set; } = 22;

    /// <summary>Placement in PHYSICAL screen px; negative == auto top-centre.</summary>
    public int PosX { get; set; } = -1;
    public int PosY { get; set; } = -1;
}

/// <summary>
/// Per-timer countdown BAR overlay configuration. A transparent, click-through, no-activate
/// window drawn over (or beside) a mirror; its fill depletes toward <see cref="DepleteFrom"/>
/// and flashes on expiry. Geometry in PHYSICAL px (principle 8).
/// </summary>
public sealed class BarConfig
{
    public bool Enabled { get; set; }

    public BarSide DepleteFrom { get; set; } = BarSide.Left;

    /// <summary>Placement/size in PHYSICAL screen px.</summary>
    public int PosX { get; set; } = 200;
    public int PosY { get; set; } = 200;
    public int Width { get; set; } = 260;
    public int Height { get; set; } = 26;

    public string FillHex { get; set; } = "#FF4CC2FF";
    public string TrackHex { get; set; } = "#66000000";
    public string FlashHex { get; set; } = "#FFFF5252";
    public bool FlashOnExpiry { get; set; } = true;
}

/// <summary>
/// One hotkey-triggered timer. A single press starts (or retriggers) EVERY duration in
/// <see cref="DurationsMs"/> at once — the "multi-timer-per-hotkey fan-out". All resulting
/// countdowns ride the ONE shared 25 ms wall-clock ticker, each storing its own absolute
/// EndTime so the display is drift-immune (principle 4). Serialized in the shared settings
/// store under the timers key.
/// </summary>
public sealed class TimerDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Timer";

    /// <summary>The global hotkey that starts/retriggers this timer. Empty = unbound.</summary>
    public HotkeyGesture Gesture { get; set; } = HotkeyGesture.None;

    /// <summary>Fan-out durations in milliseconds; one press arms all of them.</summary>
    public List<int> DurationsMs { get; set; } = new() { 30000 };

    /// <summary>Sound played on each duration's expiry (references a <see cref="SoundEntry.Id"/>).</summary>
    public string SoundId { get; set; } = "";

    /// <summary>Loop the expiry sound (~250 ms cadence) until the dismiss hotkey.</summary>
    public bool LoopSound { get; set; }

    /// <summary>Per-timer volume 0..1, multiplied by the master volume.</summary>
    public double Volume { get; set; } = 1.0;

    public AlertConfig Alert { get; set; } = new();
    public BarConfig Bar { get; set; } = new();

    public bool Enabled { get; set; } = true;

    /// <summary>Longest duration; drives the bar overlay's full extent.</summary>
    [JsonIgnore]
    public int LongestDurationMs
    {
        get
        {
            int max = 0;
            foreach (int d in DurationsMs)
                if (d > max) max = d;
            return max <= 0 ? 1 : max;
        }
    }
}
