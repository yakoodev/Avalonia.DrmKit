using Avalonia;
using Avalonia.DrmKit.Input;
using Avalonia.Input.Raw;
using Xunit;

namespace Avalonia.DrmKit.Tests;

public class TouchFeedbackTrackerTests
{
    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Begin_adds_active_indicator_at_contact_point()
    {
        var tracker = new TouchFeedbackTracker();

        var changed = tracker.OnTouch(7, RawPointerEventType.TouchBegin, new Point(100, 200), Ms(0));

        Assert.True(changed);
        var indicator = Assert.Single(tracker.Indicators);
        Assert.Equal(7, indicator.ContactId);
        Assert.Equal(new Point(100, 200), indicator.Position);
        Assert.Equal(TouchIndicatorPhase.Active, indicator.Phase);
        Assert.False(tracker.HasFading);
        Assert.Equal(1.0, tracker.OpacityOf(indicator, Ms(500)));
        Assert.Equal(1.0, tracker.ScaleOf(indicator, Ms(500)));
    }

    [Fact]
    public void Update_moves_active_indicator()
    {
        var tracker = new TouchFeedbackTracker();
        tracker.OnTouch(1, RawPointerEventType.TouchBegin, new Point(10, 10), Ms(0));

        var changed = tracker.OnTouch(1, RawPointerEventType.TouchUpdate, new Point(50, 60), Ms(16));

        Assert.True(changed);
        Assert.Equal(new Point(50, 60), tracker.Indicators[0].Position);
    }

    [Fact]
    public void Update_at_same_position_reports_no_visual_change()
    {
        var tracker = new TouchFeedbackTracker();
        tracker.OnTouch(1, RawPointerEventType.TouchBegin, new Point(10, 10), Ms(0));

        Assert.False(tracker.OnTouch(1, RawPointerEventType.TouchUpdate, new Point(10, 10), Ms(16)));
    }

    [Fact]
    public void Update_for_unknown_contact_is_ignored()
    {
        var tracker = new TouchFeedbackTracker();

        Assert.False(tracker.OnTouch(42, RawPointerEventType.TouchUpdate, new Point(1, 1), Ms(0)));
        Assert.Equal(0, tracker.Count);
    }

    [Fact]
    public void End_starts_fade_at_final_position()
    {
        var tracker = new TouchFeedbackTracker(Ms(200));
        tracker.OnTouch(3, RawPointerEventType.TouchBegin, new Point(10, 10), Ms(0));

        var changed = tracker.OnTouch(3, RawPointerEventType.TouchEnd, new Point(30, 40), Ms(100));

        Assert.True(changed);
        Assert.True(tracker.HasFading);
        var indicator = tracker.Indicators[0];
        Assert.Equal(TouchIndicatorPhase.Fading, indicator.Phase);
        Assert.Equal(new Point(30, 40), indicator.Position);
    }

    [Fact]
    public void Fade_opacity_decreases_and_scale_grows_over_time()
    {
        var tracker = new TouchFeedbackTracker(Ms(200));
        tracker.OnTouch(1, RawPointerEventType.TouchBegin, new Point(0, 0), Ms(0));
        tracker.OnTouch(1, RawPointerEventType.TouchEnd, new Point(0, 0), Ms(1000));
        var indicator = tracker.Indicators[0];

        Assert.Equal(1.0, tracker.OpacityOf(indicator, Ms(1000)), 3);
        Assert.Equal(0.5, tracker.OpacityOf(indicator, Ms(1100)), 3);
        Assert.Equal(0.0, tracker.OpacityOf(indicator, Ms(1200)), 3);
        Assert.Equal(0.0, tracker.OpacityOf(indicator, Ms(9999)), 3); // clamped

        Assert.Equal(1.0, tracker.ScaleOf(indicator, Ms(1000)), 3);
        Assert.Equal(1.0 + (TouchFeedbackTracker.FadeEndScale - 1.0) / 2, tracker.ScaleOf(indicator, Ms(1100)), 3);
        Assert.Equal(TouchFeedbackTracker.FadeEndScale, tracker.ScaleOf(indicator, Ms(1200)), 3);
    }

    [Fact]
    public void Advance_removes_finished_fades_and_reports_remaining()
    {
        var tracker = new TouchFeedbackTracker(Ms(200));
        tracker.OnTouch(1, RawPointerEventType.TouchBegin, new Point(0, 0), Ms(0));
        tracker.OnTouch(2, RawPointerEventType.TouchBegin, new Point(5, 5), Ms(0));
        tracker.OnTouch(1, RawPointerEventType.TouchEnd, new Point(0, 0), Ms(100));
        tracker.OnTouch(2, RawPointerEventType.TouchEnd, new Point(5, 5), Ms(250));

        // Contact 1's fade finished (100 + 200 <= 320); contact 2 still fading.
        Assert.True(tracker.Advance(Ms(320)));
        var remaining = Assert.Single(tracker.Indicators);
        Assert.Equal(2, remaining.ContactId);

        Assert.False(tracker.Advance(Ms(450)));
        Assert.Equal(0, tracker.Count);
        Assert.False(tracker.HasFading);
    }

    [Fact]
    public void Updates_during_fade_are_ignored()
    {
        var tracker = new TouchFeedbackTracker(Ms(200));
        tracker.OnTouch(1, RawPointerEventType.TouchBegin, new Point(0, 0), Ms(0));
        tracker.OnTouch(1, RawPointerEventType.TouchEnd, new Point(10, 10), Ms(50));

        Assert.False(tracker.OnTouch(1, RawPointerEventType.TouchUpdate, new Point(99, 99), Ms(60)));
        Assert.Equal(new Point(10, 10), tracker.Indicators[0].Position);
    }

    [Fact]
    public void Cancel_removes_indicator_immediately_without_fade()
    {
        var tracker = new TouchFeedbackTracker();
        tracker.OnTouch(1, RawPointerEventType.TouchBegin, new Point(0, 0), Ms(0));
        tracker.OnTouch(2, RawPointerEventType.TouchBegin, new Point(5, 5), Ms(0));

        var changed = tracker.OnTouch(1, RawPointerEventType.TouchCancel, new Point(0, 0), Ms(10));

        Assert.True(changed);
        var remaining = Assert.Single(tracker.Indicators);
        Assert.Equal(2, remaining.ContactId);
        Assert.False(tracker.HasFading);
    }

    [Fact]
    public void Cancel_of_fading_indicator_removes_it_and_clears_fading_flag()
    {
        var tracker = new TouchFeedbackTracker(Ms(200));
        tracker.OnTouch(1, RawPointerEventType.TouchBegin, new Point(0, 0), Ms(0));
        tracker.OnTouch(1, RawPointerEventType.TouchEnd, new Point(0, 0), Ms(50));
        Assert.True(tracker.HasFading);

        tracker.OnTouch(1, RawPointerEventType.TouchCancel, new Point(0, 0), Ms(60));

        Assert.Equal(0, tracker.Count);
        Assert.False(tracker.HasFading);
    }

    [Fact]
    public void Multiple_contacts_have_independent_indicators()
    {
        var tracker = new TouchFeedbackTracker();
        tracker.OnTouch(10, RawPointerEventType.TouchBegin, new Point(100, 100), Ms(0));
        tracker.OnTouch(11, RawPointerEventType.TouchBegin, new Point(500, 300), Ms(5));
        tracker.OnTouch(10, RawPointerEventType.TouchUpdate, new Point(120, 110), Ms(16));

        Assert.Equal(2, tracker.Count);
        Assert.Equal(new Point(120, 110), tracker.Indicators[0].Position);
        Assert.Equal(new Point(500, 300), tracker.Indicators[1].Position);
    }

    [Fact]
    public void Rebegin_of_fading_contact_reactivates_it()
    {
        var tracker = new TouchFeedbackTracker(Ms(200));
        tracker.OnTouch(1, RawPointerEventType.TouchBegin, new Point(0, 0), Ms(0));
        tracker.OnTouch(1, RawPointerEventType.TouchEnd, new Point(0, 0), Ms(50));

        tracker.OnTouch(1, RawPointerEventType.TouchBegin, new Point(20, 20), Ms(100));

        var indicator = Assert.Single(tracker.Indicators);
        Assert.Equal(TouchIndicatorPhase.Active, indicator.Phase);
        Assert.Equal(new Point(20, 20), indicator.Position);
        Assert.False(tracker.HasFading);
    }

    [Fact]
    public void Clear_removes_everything()
    {
        var tracker = new TouchFeedbackTracker();
        tracker.OnTouch(1, RawPointerEventType.TouchBegin, new Point(0, 0), Ms(0));
        tracker.OnTouch(2, RawPointerEventType.TouchBegin, new Point(1, 1), Ms(0));
        tracker.OnTouch(2, RawPointerEventType.TouchEnd, new Point(1, 1), Ms(10));

        Assert.True(tracker.Clear());
        Assert.Equal(0, tracker.Count);
        Assert.False(tracker.HasFading);
        Assert.False(tracker.Clear());
    }
}
