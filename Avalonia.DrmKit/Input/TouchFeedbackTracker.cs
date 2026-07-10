using System;
using System.Collections.Generic;
using Avalonia.Input.Raw;

namespace Avalonia.DrmKit.Input;

/// <summary>
/// Receives per-contact touch notifications from the DRM input backend on the
/// UI thread. Implemented by the touch feedback overlay; the backend only knows
/// this interface so the coupling stays thin.
/// </summary>
internal interface ITouchFeedbackSink
{
    void OnTouch(long contactId, RawPointerEventType type, Point position);
}

/// <summary>
/// Lifecycle phase of one touch feedback indicator.
/// </summary>
internal enum TouchIndicatorPhase
{
    /// <summary>The contact is down; the indicator follows it at full opacity.</summary>
    Active,

    /// <summary>The contact ended; the indicator fades out and scales up.</summary>
    Fading
}

/// <summary>
/// State of one visual touch indicator (one per active contact).
/// </summary>
internal sealed class TouchIndicator
{
    public long ContactId;
    public Point Position;
    public TouchIndicatorPhase Phase;

    /// <summary>Clock time at which the fade-out started (meaningful in <see cref="TouchIndicatorPhase.Fading"/>).</summary>
    public TimeSpan FadeStart;
}

/// <summary>
/// Pure state machine behind the touch feedback overlay: maps raw touch events
/// (Begin/Update/End/Cancel per contact id) to a set of indicators with a
/// fade-out lifecycle. Time is passed in explicitly so the fade behavior is
/// unit-testable without timers; rendering and timer scheduling live in
/// <see cref="TouchFeedbackLayer"/>.
/// </summary>
internal sealed class TouchFeedbackTracker
{
    public static readonly TimeSpan DefaultFadeDuration = TimeSpan.FromMilliseconds(200);

    /// <summary>Scale factor the indicator grows to at the end of the fade.</summary>
    public const double FadeEndScale = 1.35;

    private readonly List<TouchIndicator> _indicators = new();
    private readonly TimeSpan _fadeDuration;
    private int _fadingCount;

    public TouchFeedbackTracker(TimeSpan? fadeDuration = null)
    {
        _fadeDuration = fadeDuration is { } d && d > TimeSpan.Zero ? d : DefaultFadeDuration;
    }

    public TimeSpan FadeDuration => _fadeDuration;

    /// <summary>All live indicators (active and fading). Iterate by index to avoid allocations.</summary>
    public IReadOnlyList<TouchIndicator> Indicators => _indicators;

    public int Count => _indicators.Count;

    /// <summary>True while at least one indicator is fading (the overlay needs its animation timer).</summary>
    public bool HasFading => _fadingCount > 0;

    /// <summary>
    /// Applies one raw touch event. Returns true when the visual state changed
    /// and the overlay should re-render.
    /// </summary>
    public bool OnTouch(long contactId, RawPointerEventType type, Point position, TimeSpan now)
    {
        switch (type)
        {
            case RawPointerEventType.TouchBegin:
            {
                var existing = Find(contactId);
                if (existing is null)
                {
                    _indicators.Add(new TouchIndicator
                    {
                        ContactId = contactId,
                        Position = position,
                        Phase = TouchIndicatorPhase.Active
                    });
                }
                else
                {
                    // Re-begin of a known id (e.g. reseed after resync): reset to active.
                    if (existing.Phase == TouchIndicatorPhase.Fading)
                        _fadingCount--;
                    existing.Position = position;
                    existing.Phase = TouchIndicatorPhase.Active;
                }

                return true;
            }

            case RawPointerEventType.TouchUpdate:
            {
                var indicator = Find(contactId);
                if (indicator is null || indicator.Phase != TouchIndicatorPhase.Active)
                    return false;

                if (indicator.Position == position)
                    return false;

                indicator.Position = position;
                return true;
            }

            case RawPointerEventType.TouchEnd:
            {
                var indicator = Find(contactId);
                if (indicator is null || indicator.Phase == TouchIndicatorPhase.Fading)
                    return false;

                indicator.Position = position;
                indicator.Phase = TouchIndicatorPhase.Fading;
                indicator.FadeStart = now;
                _fadingCount++;
                return true;
            }

            case RawPointerEventType.TouchCancel:
            {
                // Cancels (SYN_DROPPED resync, device removal) clear immediately —
                // the contact state is unreliable, so no fade-out.
                for (var i = 0; i < _indicators.Count; i++)
                {
                    if (_indicators[i].ContactId != contactId)
                        continue;

                    if (_indicators[i].Phase == TouchIndicatorPhase.Fading)
                        _fadingCount--;
                    _indicators.RemoveAt(i);
                    return true;
                }

                return false;
            }

            default:
                return false;
        }
    }

    /// <summary>
    /// Advances fade-outs and removes finished indicators. Returns true while
    /// any indicator is still fading (i.e. the animation timer must keep running).
    /// </summary>
    public bool Advance(TimeSpan now)
    {
        for (var i = _indicators.Count - 1; i >= 0; i--)
        {
            var indicator = _indicators[i];
            if (indicator.Phase != TouchIndicatorPhase.Fading)
                continue;

            if (now - indicator.FadeStart >= _fadeDuration)
            {
                _indicators.RemoveAt(i);
                _fadingCount--;
            }
        }

        return _fadingCount > 0;
    }

    /// <summary>Removes all indicators immediately.</summary>
    public bool Clear()
    {
        if (_indicators.Count == 0)
            return false;

        _indicators.Clear();
        _fadingCount = 0;
        return true;
    }

    /// <summary>Opacity multiplier (0..1) for the indicator at the given time.</summary>
    public double OpacityOf(TouchIndicator indicator, TimeSpan now)
    {
        if (indicator.Phase != TouchIndicatorPhase.Fading)
            return 1.0;

        var t = FadeProgress(indicator, now);
        return 1.0 - t;
    }

    /// <summary>Scale multiplier (1..FadeEndScale) for the indicator at the given time.</summary>
    public double ScaleOf(TouchIndicator indicator, TimeSpan now)
    {
        if (indicator.Phase != TouchIndicatorPhase.Fading)
            return 1.0;

        var t = FadeProgress(indicator, now);
        return 1.0 + (FadeEndScale - 1.0) * t;
    }

    private double FadeProgress(TouchIndicator indicator, TimeSpan now)
    {
        var t = (now - indicator.FadeStart).TotalMilliseconds / _fadeDuration.TotalMilliseconds;
        return Math.Clamp(t, 0.0, 1.0);
    }

    private TouchIndicator? Find(long contactId)
    {
        for (var i = 0; i < _indicators.Count; i++)
        {
            if (_indicators[i].ContactId == contactId)
                return _indicators[i];
        }

        return null;
    }
}
