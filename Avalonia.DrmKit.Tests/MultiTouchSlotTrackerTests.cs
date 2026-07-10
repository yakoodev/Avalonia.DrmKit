using Avalonia.DrmKit.Input;
using Xunit;

namespace Avalonia.DrmKit.Tests;

public class MultiTouchSlotTrackerTests
{
    private static void Begin(MultiTouchSlotTracker tracker, int slot, int trackingId, double x, double y)
    {
        tracker.SelectSlot(slot);
        tracker.ProcessTrackingId(trackingId);
        tracker.SetScreenX(x);
        tracker.SetScreenY(y);
    }

    private static void Move(MultiTouchSlotTracker tracker, int slot, double x, double y)
    {
        tracker.SelectSlot(slot);
        tracker.SetScreenX(x);
        tracker.SetScreenY(y);
    }

    private static void End(MultiTouchSlotTracker tracker, int slot)
    {
        tracker.SelectSlot(slot);
        tracker.ProcessTrackingId(-1);
    }

    private static void AssertEvent(
        SlotContactEvent e, SlotContactEventKind kind, int slot, long trackingId, double x, double y)
    {
        Assert.Equal(kind, e.Kind);
        Assert.Equal(slot, e.SlotIndex);
        Assert.Equal(trackingId, e.TrackingId);
        Assert.Equal(x, e.X);
        Assert.Equal(y, e.Y);
    }

    [Fact]
    public void Two_finger_sequence_produces_correct_per_contact_events()
    {
        var tracker = new MultiTouchSlotTracker(5);

        // Frame 1: first finger down in slot 0.
        Begin(tracker, 0, 7, 100, 100);
        var frame = tracker.CommitFrame();
        var e = Assert.Single(frame);
        AssertEvent(e, SlotContactEventKind.Begin, 0, 7, 100, 100);
        Assert.Equal(1, tracker.ActiveContactCount);
        Assert.Equal(0, tracker.PrimarySlot);

        // Frame 2: second finger down in slot 1.
        Begin(tracker, 1, 9, 200, 220);
        frame = tracker.CommitFrame();
        e = Assert.Single(frame);
        AssertEvent(e, SlotContactEventKind.Begin, 1, 9, 200, 220);
        Assert.Equal(2, tracker.ActiveContactCount);
        Assert.Equal(0, tracker.PrimarySlot); // primary does not transfer

        // Frame 3: both fingers move within the same frame.
        Move(tracker, 0, 110, 105);
        Move(tracker, 1, 210, 230);
        frame = tracker.CommitFrame();
        Assert.Equal(2, frame.Count);
        AssertEvent(frame[0], SlotContactEventKind.Update, 0, 7, 110, 105);
        AssertEvent(frame[1], SlotContactEventKind.Update, 1, 9, 210, 230);

        // Frame 4: second finger up.
        End(tracker, 1);
        frame = tracker.CommitFrame();
        e = Assert.Single(frame);
        AssertEvent(e, SlotContactEventKind.End, 1, 9, 210, 230);
        Assert.Equal(1, tracker.ActiveContactCount);

        // Frame 5: first finger up.
        End(tracker, 0);
        frame = tracker.CommitFrame();
        e = Assert.Single(frame);
        AssertEvent(e, SlotContactEventKind.End, 0, 7, 110, 105);
        Assert.Equal(0, tracker.ActiveContactCount);
    }

    [Fact]
    public void Slot_switching_mid_frame_commits_both_contacts_in_one_frame()
    {
        var tracker = new MultiTouchSlotTracker(5);

        // Both fingers land within a single evdev packet.
        Begin(tracker, 0, 1, 10, 20);
        Begin(tracker, 1, 2, 30, 40);

        var frame = tracker.CommitFrame();

        Assert.Equal(2, frame.Count);
        AssertEvent(frame[0], SlotContactEventKind.Begin, 0, 1, 10, 20);
        AssertEvent(frame[1], SlotContactEventKind.Begin, 1, 2, 30, 40);
        Assert.Equal(2, tracker.ActiveContactCount);
    }

    [Fact]
    public void Tracking_id_reuse_after_release_starts_a_fresh_contact()
    {
        var tracker = new MultiTouchSlotTracker(5);

        Begin(tracker, 0, 5, 50, 60);
        tracker.CommitFrame();
        End(tracker, 0);
        tracker.CommitFrame();

        // The kernel reuses tracking id 5 for a new contact in the same slot.
        // Protocol B: unchanged slot values are not resent, so the Begin reuses
        // the persisted position.
        tracker.SelectSlot(0);
        tracker.ProcessTrackingId(5);
        var frame = tracker.CommitFrame();

        var e = Assert.Single(frame);
        AssertEvent(e, SlotContactEventKind.Begin, 0, 5, 50, 60);
        Assert.Equal(1, tracker.ActiveContactCount);
    }

    [Fact]
    public void Frame_with_only_one_slot_changing_emits_a_single_update()
    {
        var tracker = new MultiTouchSlotTracker(5);

        Begin(tracker, 0, 1, 10, 10);
        Begin(tracker, 1, 2, 90, 90);
        tracker.CommitFrame();

        // Only the second finger moves.
        Move(tracker, 1, 95, 92);
        var frame = tracker.CommitFrame();

        var e = Assert.Single(frame);
        AssertEvent(e, SlotContactEventKind.Update, 1, 2, 95, 92);
    }

    [Fact]
    public void Cancel_all_cancels_both_contacts_and_resets_state()
    {
        var tracker = new MultiTouchSlotTracker(5);

        Begin(tracker, 0, 3, 10, 10);
        Begin(tracker, 1, 4, 20, 20);
        tracker.CommitFrame();

        var cancels = tracker.CancelAll();

        Assert.Equal(2, cancels.Count);
        AssertEvent(cancels[0], SlotContactEventKind.Cancel, 0, 3, 10, 10);
        AssertEvent(cancels[1], SlotContactEventKind.Cancel, 1, 4, 20, 20);
        Assert.Equal(0, tracker.ActiveContactCount);
        Assert.Equal(-1, tracker.PrimarySlot);

        // No stale changes survive the reset.
        Assert.Empty(tracker.CommitFrame());

        // A new contact after the resync begins cleanly.
        Begin(tracker, 0, 8, 33, 44);
        var frame = tracker.CommitFrame();
        var e = Assert.Single(frame);
        AssertEvent(e, SlotContactEventKind.Begin, 0, 8, 33, 44);
    }

    [Fact]
    public void Cancel_all_reports_unannounced_contacts_only_if_previously_announced()
    {
        var tracker = new MultiTouchSlotTracker(5);

        // Contact begun but never committed (no position yet): downstream never
        // saw it, so no Cancel must be produced.
        tracker.SelectSlot(0);
        tracker.ProcessTrackingId(11);
        Assert.Empty(tracker.CancelAll());

        // Announced contact that ended in the current (uncommitted) frame is
        // still known downstream and must be cancelled.
        Begin(tracker, 0, 12, 5, 5);
        tracker.CommitFrame();
        End(tracker, 0);
        var cancels = tracker.CancelAll();
        var e = Assert.Single(cancels);
        AssertEvent(e, SlotContactEventKind.Cancel, 0, 12, 5, 5);
    }

    [Fact]
    public void Slot_count_falls_back_to_ten_when_unknown()
    {
        Assert.Equal(MultiTouchSlotTracker.FallbackSlotCount, new MultiTouchSlotTracker(0).SlotCount);
        Assert.Equal(MultiTouchSlotTracker.FallbackSlotCount, new MultiTouchSlotTracker(-3).SlotCount);
        Assert.Equal(4, new MultiTouchSlotTracker(4).SlotCount);
    }

    [Fact]
    public void Selecting_a_slot_beyond_capacity_grows_the_tracker()
    {
        var tracker = new MultiTouchSlotTracker(2);

        Begin(tracker, 7, 42, 1, 2);
        var frame = tracker.CommitFrame();

        Assert.True(tracker.SlotCount >= 8);
        var e = Assert.Single(frame);
        AssertEvent(e, SlotContactEventKind.Begin, 7, 42, 1, 2);
    }

    [Fact]
    public void Negative_slot_selection_is_ignored()
    {
        var tracker = new MultiTouchSlotTracker(3);

        tracker.SelectSlot(1);
        tracker.SelectSlot(-2);

        Assert.Equal(1, tracker.CurrentSlot);
    }

    [Fact]
    public void Begin_is_deferred_until_both_coordinates_are_known()
    {
        var tracker = new MultiTouchSlotTracker(5);

        tracker.SelectSlot(0);
        tracker.ProcessTrackingId(6);
        tracker.SetScreenX(15); // Y still unknown (e.g. axis not calibrated yet)
        Assert.Empty(tracker.CommitFrame());

        tracker.SelectSlot(0);
        tracker.SetScreenY(25);
        var frame = tracker.CommitFrame();

        var e = Assert.Single(frame);
        AssertEvent(e, SlotContactEventKind.Begin, 0, 6, 15, 25);
    }

    [Fact]
    public void Lift_frame_flushes_final_position_before_end()
    {
        var tracker = new MultiTouchSlotTracker(5);

        Begin(tracker, 0, 1, 10, 10);
        tracker.CommitFrame();

        // Final movement and the lift arrive within the same frame.
        Move(tracker, 0, 50, 60);
        End(tracker, 0);
        var frame = tracker.CommitFrame();

        Assert.Equal(2, frame.Count);
        AssertEvent(frame[0], SlotContactEventKind.Update, 0, 1, 50, 60);
        Assert.True(frame[0].IsLiftFlush);
        AssertEvent(frame[1], SlotContactEventKind.End, 0, 1, 50, 60);
        Assert.False(frame[1].IsLiftFlush);
    }

    [Fact]
    public void Begin_and_end_within_one_frame_emit_a_full_pair()
    {
        var tracker = new MultiTouchSlotTracker(5);

        Begin(tracker, 0, 21, 70, 80);
        End(tracker, 0);
        var frame = tracker.CommitFrame();

        Assert.Equal(2, frame.Count);
        AssertEvent(frame[0], SlotContactEventKind.Begin, 0, 21, 70, 80);
        AssertEvent(frame[1], SlotContactEventKind.End, 0, 21, 70, 80);
        Assert.Equal(0, tracker.ActiveContactCount);
    }

    [Fact]
    public void Contact_replacement_without_minus_one_ends_the_old_contact_first()
    {
        var tracker = new MultiTouchSlotTracker(5);

        Begin(tracker, 0, 1, 10, 10);
        tracker.CommitFrame();

        // New tracking id in the occupied slot without an explicit -1.
        tracker.SelectSlot(0);
        tracker.ProcessTrackingId(2);
        var frame = tracker.CommitFrame();

        Assert.Equal(2, frame.Count);
        AssertEvent(frame[0], SlotContactEventKind.End, 0, 1, 10, 10);
        AssertEvent(frame[1], SlotContactEventKind.Begin, 0, 2, 10, 10);
        Assert.Equal(1, tracker.ActiveContactCount);
    }

    [Fact]
    public void Primary_slot_is_not_transferred_and_not_stolen_by_slot_reuse()
    {
        var tracker = new MultiTouchSlotTracker(5);

        Begin(tracker, 0, 1, 1, 1);
        tracker.CommitFrame();
        Begin(tracker, 1, 2, 2, 2);
        tracker.CommitFrame();
        Assert.Equal(0, tracker.PrimarySlot);

        // Primary lifts; the surviving secondary must not become primary.
        End(tracker, 0);
        tracker.CommitFrame();
        Assert.NotEqual(1, tracker.PrimarySlot);

        // A new contact reusing the freed primary slot while the secondary is
        // still down must not become primary either (cursor must not warp).
        Begin(tracker, 0, 3, 4, 4);
        tracker.CommitFrame();
        Assert.Equal(-1, tracker.PrimarySlot);

        // Once all contacts lift, the next touch session gets a fresh primary.
        End(tracker, 0);
        End(tracker, 1);
        tracker.CommitFrame();
        Begin(tracker, 2, 9, 5, 5);
        tracker.CommitFrame();
        Assert.Equal(2, tracker.PrimarySlot);
    }

    [Fact]
    public void Single_touch_emulation_is_suppressed_only_for_slot_devices()
    {
        // Devices without ABS_MT_SLOT keep the legacy single-touch path.
        Assert.False(MultiTouchSlotTracker.SuppressesSingleTouchEmulation(null));

        // Devices with MT slots duplicate contact 0 via ABS_X/ABS_Y/BTN_TOUCH;
        // those events must be ignored for touch synthesis.
        Assert.True(MultiTouchSlotTracker.SuppressesSingleTouchEmulation(new MultiTouchSlotTracker(5)));
    }

    [Fact]
    public void Repeated_tracking_id_announcement_is_a_no_op()
    {
        var tracker = new MultiTouchSlotTracker(5);

        Begin(tracker, 0, 1, 10, 10);
        tracker.CommitFrame();

        tracker.SelectSlot(0);
        tracker.ProcessTrackingId(1);
        Assert.Empty(tracker.CommitFrame());
        Assert.Equal(1, tracker.ActiveContactCount);
    }

    [Fact]
    public void End_on_empty_slot_is_ignored()
    {
        var tracker = new MultiTouchSlotTracker(5);

        tracker.SelectSlot(2);
        tracker.ProcessTrackingId(-1);

        Assert.Empty(tracker.CommitFrame());
        Assert.Equal(0, tracker.ActiveContactCount);
    }
}
