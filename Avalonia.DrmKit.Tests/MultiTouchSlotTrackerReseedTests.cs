using Avalonia.DrmKit.Input;
using Xunit;

namespace Avalonia.DrmKit.Tests;

/// <summary>
/// Tests for the EVIOCGMTSLOTS re-seed path used by SYN_DROPPED recovery:
/// after CancelAll, contacts that the kernel still reports as down are
/// re-announced as Begin at their current positions.
/// </summary>
public class MultiTouchSlotTrackerReseedTests
{
    private static void Begin(MultiTouchSlotTracker tracker, int slot, int trackingId, double x, double y)
    {
        tracker.SelectSlot(slot);
        tracker.ProcessTrackingId(trackingId);
        tracker.SetScreenX(x);
        tracker.SetScreenY(y);
    }

    private static SlotResyncState Held(long id, double x, double y)
        => new() { TrackingId = id, HasPosition = true, X = x, Y = y };

    private static SlotResyncState HeldNoPosition(long id)
        => new() { TrackingId = id, HasPosition = false };

    [Fact]
    public void Reseed_reannounces_held_contacts_at_kernel_positions()
    {
        var tracker = new MultiTouchSlotTracker(5);
        Begin(tracker, 0, 7, 100, 100);
        Begin(tracker, 1, 9, 200, 220);
        tracker.CommitFrame();

        var cancelled = tracker.CancelAll();
        Assert.Equal(2, cancelled.Count);
        Assert.Equal(0, tracker.ActiveContactCount);

        var slots = new[]
        {
            Held(7, 105, 102),
            Held(9, 210, 230),
            SlotResyncState.Empty,
            SlotResyncState.Empty,
            SlotResyncState.Empty
        };

        var begins = tracker.Reseed(slots);

        Assert.Equal(2, begins.Count);
        Assert.Equal(SlotContactEventKind.Begin, begins[0].Kind);
        Assert.Equal(7, begins[0].TrackingId);
        Assert.Equal(105, begins[0].X);
        Assert.Equal(102, begins[0].Y);
        Assert.Equal(SlotContactEventKind.Begin, begins[1].Kind);
        Assert.Equal(9, begins[1].TrackingId);

        Assert.Equal(2, tracker.ActiveContactCount);
        Assert.Equal(0, tracker.PrimarySlot);
    }

    [Fact]
    public void Reseed_skips_empty_slots()
    {
        var tracker = new MultiTouchSlotTracker(3);
        Begin(tracker, 0, 5, 50, 50);
        tracker.CommitFrame();
        tracker.CancelAll();

        // The kernel says the contact lifted while events were dropped.
        var begins = tracker.Reseed(new[]
        {
            SlotResyncState.Empty,
            SlotResyncState.Empty,
            SlotResyncState.Empty
        });

        Assert.Empty(begins);
        Assert.Equal(0, tracker.ActiveContactCount);
        Assert.Equal(-1, tracker.PrimarySlot);
    }

    [Fact]
    public void Reseed_uses_persisted_slot_position_when_kernel_position_is_unavailable()
    {
        var tracker = new MultiTouchSlotTracker(3);
        Begin(tracker, 1, 4, 300, 400);
        tracker.CommitFrame();
        tracker.CancelAll(); // protocol B slot positions persist

        var begins = tracker.Reseed(new[]
        {
            SlotResyncState.Empty,
            HeldNoPosition(4),
            SlotResyncState.Empty
        });

        var begin = Assert.Single(begins);
        Assert.Equal(SlotContactEventKind.Begin, begin.Kind);
        Assert.Equal(4, begin.TrackingId);
        Assert.Equal(300, begin.X);
        Assert.Equal(400, begin.Y);
        Assert.Equal(1, tracker.ActiveContactCount);
        Assert.Equal(1, tracker.PrimarySlot);
    }

    [Fact]
    public void Reseed_defers_begin_when_no_position_is_known()
    {
        var tracker = new MultiTouchSlotTracker(2);

        // Contact appears during the dropped window; the tracker never saw it
        // and no position could be mapped (uncalibrated axis).
        var begins = tracker.Reseed(new[]
        {
            HeldNoPosition(11),
            SlotResyncState.Empty
        });

        Assert.Empty(begins);
        Assert.Equal(1, tracker.ActiveContactCount);

        // The deferred Begin commits once a position arrives.
        tracker.SelectSlot(0);
        tracker.SetScreenX(10);
        tracker.SetScreenY(20);
        var frame = tracker.CommitFrame();

        var e = Assert.Single(frame);
        Assert.Equal(SlotContactEventKind.Begin, e.Kind);
        Assert.Equal(11, e.TrackingId);
        Assert.Equal(10, e.X);
        Assert.Equal(20, e.Y);
    }

    [Fact]
    public void Repeated_tracking_id_after_reseed_does_not_begin_again()
    {
        var tracker = new MultiTouchSlotTracker(2);
        Begin(tracker, 0, 3, 100, 100);
        tracker.CommitFrame();
        tracker.CancelAll();
        var begins = tracker.Reseed(new[] { Held(3, 100, 100), SlotResyncState.Empty });
        Assert.Single(begins);

        // The first frame after resync re-reports the same tracking id (the
        // kernel state and the queued events overlap) — it must not duplicate.
        tracker.SelectSlot(0);
        tracker.ProcessTrackingId(3);
        var frame = tracker.CommitFrame();

        Assert.Empty(frame);
        Assert.Equal(1, tracker.ActiveContactCount);
    }

    [Fact]
    public void Reseed_contact_moves_and_ends_normally_afterwards()
    {
        var tracker = new MultiTouchSlotTracker(2);
        Begin(tracker, 0, 8, 100, 100);
        tracker.CommitFrame();
        tracker.CancelAll();
        tracker.Reseed(new[] { Held(8, 120, 130), SlotResyncState.Empty });

        tracker.SelectSlot(0);
        tracker.SetScreenX(150);
        tracker.SetScreenY(160);
        var frame = tracker.CommitFrame();
        var update = Assert.Single(frame);
        Assert.Equal(SlotContactEventKind.Update, update.Kind);
        Assert.Equal(150, update.X);

        tracker.SelectSlot(0);
        tracker.ProcessTrackingId(-1);
        frame = tracker.CommitFrame();
        var end = Assert.Single(frame);
        Assert.Equal(SlotContactEventKind.End, end.Kind);
        Assert.Equal(8, end.TrackingId);
        Assert.Equal(0, tracker.ActiveContactCount);
    }

    [Fact]
    public void Reseed_ignores_kernel_slots_beyond_tracker_capacity()
    {
        var tracker = new MultiTouchSlotTracker(2);

        var begins = tracker.Reseed(new[]
        {
            Held(1, 10, 10),
            SlotResyncState.Empty,
            Held(2, 20, 20), // beyond capacity — ignored defensively
        });

        var begin = Assert.Single(begins);
        Assert.Equal(1, begin.TrackingId);
        Assert.Equal(1, tracker.ActiveContactCount);
    }
}
