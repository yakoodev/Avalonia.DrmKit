using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;

namespace Avalonia.DrmKit.Input;

/// <summary>
/// Overlay control that draws one semi-transparent circle per active touch
/// contact (multi-contact visual feedback for kiosk touchscreens). Attached to
/// the same TopLevel host panel as the soft cursor by
/// <see cref="TouchFeedbackInstaller"/> and driven by the DRM input backend
/// through <see cref="ITouchFeedbackSink"/> (via <see cref="TouchFeedbackHub"/>).
///
/// Rendering is a plain <see cref="Render"/> override with reused immutable
/// brush/pen instances, so steady-state per-frame allocations are zero. A
/// ~60 Hz <see cref="DispatcherTimer"/> runs only while at least one indicator
/// is fading out; there is no permanent timer.
/// </summary>
internal sealed class TouchFeedbackLayer : Control, ITouchFeedbackSink
{
    private const double RingThickness = 2.0;

    private readonly TouchFeedbackTracker _tracker = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly double _radius;
    private readonly ImmutableSolidColorBrush _fill;
    private readonly ImmutablePen _ring;

    private DispatcherTimer? _fadeTimer;

    public TouchFeedbackLayer(double sizePx, double fillOpacity)
    {
        IsHitTestVisible = false;
        Focusable = false;

        if (sizePx <= 0 || double.IsNaN(sizePx))
            sizePx = 44;
        fillOpacity = double.IsNaN(fillOpacity) ? 0.38 : Math.Clamp(fillOpacity, 0.0, 1.0);

        _radius = sizePx / 2.0;

        // Theme-agnostic: a light low-opacity disc with a brighter ring reads on
        // both dark kiosk UIs and light themes.
        var fillAlpha = (byte)Math.Round(fillOpacity * 255);
        var ringAlpha = (byte)Math.Round(Math.Min(1.0, fillOpacity + 0.35) * 255);

        _fill = new ImmutableSolidColorBrush(Color.FromArgb(fillAlpha, 255, 255, 255));
        _ring = new ImmutablePen(
            new ImmutableSolidColorBrush(Color.FromArgb(ringAlpha, 255, 255, 255)),
            RingThickness);
    }

    internal TouchFeedbackTracker Tracker => _tracker;

    /// <summary>Backend touch notification; runs on the UI thread.</summary>
    public void OnTouch(long contactId, RawPointerEventType type, Point position)
    {
        var changed = _tracker.OnTouch(contactId, type, position, _clock.Elapsed);

        if (_tracker.HasFading)
            EnsureFadeTimer();

        if (changed)
            InvalidateVisual();
    }

    /// <summary>Removes every indicator immediately (e.g. all contacts cancelled).</summary>
    public void ClearAll()
    {
        _fadeTimer?.Stop();
        if (_tracker.Clear())
            InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var indicators = _tracker.Indicators;
        if (indicators.Count == 0)
            return;

        var now = _clock.Elapsed;
        for (var i = 0; i < indicators.Count; i++)
        {
            var indicator = indicators[i];
            var opacity = _tracker.OpacityOf(indicator, now);
            if (opacity <= 0)
                continue;

            var radius = _radius * _tracker.ScaleOf(indicator, now);

            using (context.PushOpacity(opacity))
            {
                context.DrawEllipse(_fill, _ring, indicator.Position, radius, radius);
            }
        }
    }

    private void EnsureFadeTimer()
    {
        if (_fadeTimer is null)
        {
            _fadeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16) // ~60 FPS, only while fading
            };
            _fadeTimer.Tick += FadeTimerTick;
        }

        if (!_fadeTimer.IsEnabled)
            _fadeTimer.Start();
    }

    private void FadeTimerTick(object? sender, EventArgs e)
    {
        var stillFading = _tracker.Advance(_clock.Elapsed);
        InvalidateVisual();

        if (!stillFading)
            _fadeTimer?.Stop();
    }
}
