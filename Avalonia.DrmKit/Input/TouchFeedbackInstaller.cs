using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Avalonia.DrmKit.Input;

/// <summary>
/// Attaches the <see cref="TouchFeedbackLayer"/> overlay to the TopLevel that
/// hosts the DRM input root — the same host-panel pattern used by
/// <c>SoftCursorInstaller</c>. Invoked by the input backend when the input root
/// is assigned; retries briefly on a UI timer because the visual tree may not
/// be built yet at that point.
/// </summary>
internal static class TouchFeedbackInstaller
{
    private const string FlagKey = "__DrmKit_TouchFeedbackAttached";
    private const int MaxAttempts = 100; // 100 x 200 ms = 20 s

    /// <summary>
    /// Schedules attaching the overlay for the given input root on the UI thread.
    /// Safe to call multiple times and from any thread.
    /// </summary>
    public static void ScheduleAttach(IInputRoot inputRoot, DrmTouchOptions options)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var tl = ResolveTopLevel(inputRoot);
            if (tl is null)
                return;

            if (TryAttach(tl, options))
                return;

            // The content panel may not exist yet; retry briefly.
            var attempts = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            timer.Tick += (_, _) =>
            {
                if (TryAttach(tl, options) || ++attempts >= MaxAttempts)
                    timer.Stop();
            };
            timer.Start();
        }, DispatcherPriority.Background);
    }

    private static TopLevel? ResolveTopLevel(IInputRoot inputRoot)
    {
        if (inputRoot is TopLevel tl)
            return tl;

        if (inputRoot is Visual visual)
            return TopLevel.GetTopLevel(visual);

        return null;
    }

    private static bool TryAttach(TopLevel tl, DrmTouchOptions options)
    {
        if (tl.Resources.ContainsKey(FlagKey))
            return true;

        var host = tl.GetVisualDescendants().OfType<Panel>().FirstOrDefault();
        if (host is null)
            return false;

        tl.Resources[FlagKey] = true;

        var overlay = new TouchFeedbackLayer(options.TouchFeedbackSizePx, options.TouchFeedbackOpacity)
        {
            Width = double.NaN,
            Height = double.NaN,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        host.Children.Add(overlay);
        TouchFeedbackHub.Register(tl, overlay);
        Console.WriteLine("[DrmKit] TouchFeedbackInstaller: overlay added");
        return true;
    }
}
