using Microsoft.Win32;

namespace Echo.Bridge.Windows;

/// <summary>Native semantic roles, following the Windows app theme and high contrast.</summary>
internal sealed record BridgeTheme(Color Background, Color Surface, Color Field, Color Text, Color Muted,
    Color Outline, Color Accent, Color OnAccent, Color Positive, Color Warning)
{
    public static BridgeTheme Resolve(bool? dark = null)
    {
        if (SystemInformation.HighContrast) return new(SystemColors.Window, SystemColors.Window, SystemColors.Window,
            SystemColors.WindowText, SystemColors.GrayText, SystemColors.WindowText, SystemColors.Highlight,
            SystemColors.HighlightText, SystemColors.WindowText, SystemColors.WindowText);
        if (dark is null)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                dark = key?.GetValue("AppsUseLightTheme") is int value && value == 0;
            }
            catch { dark = false; }
        }
        return dark == true
            ? new(Color.FromArgb(32, 32, 32), Color.FromArgb(38, 38, 38), Color.FromArgb(43, 43, 43),
                Color.FromArgb(240, 240, 240), Color.FromArgb(175, 175, 175), Color.FromArgb(64, 64, 64),
                Color.FromArgb(176, 211, 225), Color.FromArgb(19, 39, 47), Color.FromArgb(134, 211, 170), Color.FromArgb(238, 196, 120))
            : new(Color.FromArgb(250, 250, 250), Color.White, Color.White,
                Color.FromArgb(32, 32, 32), Color.FromArgb(106, 106, 106), Color.FromArgb(224, 224, 224),
                Color.FromArgb(35, 69, 86), Color.White, Color.FromArgb(33, 116, 79), Color.FromArgb(137, 91, 19));
    }
}
