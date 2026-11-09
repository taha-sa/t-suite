using System.Drawing;

namespace TSuite.Update;

/// <summary>Uygulamanin kendi paletini tasiyan tema / per-app palette for the UpdateCard.</summary>
public sealed record UpdateTheme(
    Color Bg, Color Surface, Color Border, Color Accent, Color AccentHover, Color Text, Color TextDim)
{
    /// <summary>Accent uzerindeki yazi rengi (varsayilan beyaz).</summary>
    public Color OnAccent { get; init; } = Color.White;

    public static UpdateTheme Dark { get; } = new(
        Color.FromArgb(0x1E, 0x1E, 0x24), Color.FromArgb(0x2A, 0x2A, 0x32), Color.FromArgb(0x3A, 0x3A, 0x46),
        Color.FromArgb(0x4C, 0x8D, 0xFF), Color.FromArgb(0x6B, 0xA2, 0xFF), Color.FromArgb(0xEC, 0xEC, 0xF1), Color.FromArgb(0x9A, 0x9A, 0xA8));
}

