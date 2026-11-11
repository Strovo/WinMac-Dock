using System;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinMac.Dock.Core;

namespace WinMac.Dock;

// Part of the dock's existing visual tree: no HWND, backdrop or drag behavior.
internal sealed class DockClockView : Grid
{
    private readonly Func<DesktopClockSettings> settings;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    internal TextBlock TimeLabel { get; } = new()
    {
        FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch
    };
    internal TextBlock DateLabel { get; } = new()
    {
        FontSize = 11, Opacity = .75, TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };

    internal DockClockView(Func<DesktopClockSettings> settings)
    {
        this.settings = settings;
        AutomationProperties.SetName(this, "Dock clock");
        var content = new Grid { Width = 96, Height = 36 };
        content.RowDefinitions.Add(new() { Height = new GridLength(21) });
        content.RowDefinitions.Add(new() { Height = new GridLength(15) });
        Grid.SetRow(DateLabel, 1);
        content.Children.Add(TimeLabel); content.Children.Add(DateLabel);
        Children.Add(new Viewbox { Child = content, Stretch = Stretch.Uniform,
            Margin = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center });
        timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { Refresh(); if (settings().Enabled) timer.Start(); };
        Unloaded += (_, _) => timer.Stop();
    }

    internal void Configure(bool vertical, double buttonSize, double crossSize)
    {
        var options = settings();
        Visibility = options.Enabled ? Visibility.Visible : Visibility.Collapsed;
        Width = vertical ? crossSize : DesktopClock.AlongLength(options, false, buttonSize);
        Height = vertical ? buttonSize : crossSize;
        Refresh();
        if (options.Enabled && IsLoaded) timer.Start(); else timer.Stop();
    }

    internal void Stop() => timer.Stop();

    private void Refresh()
    {
        var now = DateTime.Now;
        var culture = CultureInfo.CurrentCulture;
        TimeLabel.Text = DesktopClock.TimeText(now, settings(), culture);
        DateLabel.Text = DesktopClock.CompactDateText(now, culture);
        ToolTipService.SetToolTip(this, DesktopClock.DateText(now, culture));
    }
}
