using Microsoft.UI.Xaml;

namespace WinMac.Dock;

internal sealed partial class DockWindow
{
    private double ClockReservation => !Settings.DesktopClock.Enabled ? 0
        : DesktopClock.AlongLength(Settings.DesktopClock, Vertical,
            Settings.IconSize + (Settings.WindowsTaskbarHighlights ? 20 : 12)) + Math.Max(0, Settings.IconSpacing);

    private double DockElementLength(FrameworkElement element) => ReferenceEquals(element, ClockView)
        ? DesktopClock.AlongLength(Settings.DesktopClock, Vertical, ButtonSize)
        : element is Microsoft.UI.Xaml.Controls.Button ? ButtonSize : SeparatorWidth;
}
