using System;
using System.Collections.Generic;

namespace Avalonia.DrmKit.Input;

/// <summary>
/// Kind of per-contact change produced by <see cref="MultiTouchSlotTracker"/>.
/// </summary>
internal enum SlotContactEventKind
{
    /// <summary>A new contact began (tracking id assigned and a position is known).</summary>
    Begin,

    /// <summary>An active contact moved.</summary>
    Update,

    /// <summary>A contact ended (tracking id set to -1).</summary>
    End,

    /// <summary>A contact was cancelled (SYN_DROPPED resync).</summary>
    Cancel
}

/// <summary>
/// One touch-contact change committed at a frame boundary (SYN_REPORT).
/// Coordinates are already-mapped screen coordinates.
/// </summary>
internal readonly struct SlotContactEvent
{
    public SlotContactEventKind Kind { get; init; }
    public int SlotIndex { get; init; }
    public long TrackingId { get; init; }
    public double X { get; init; }
    public double Y { get; init; }

    /// <summary>
    /// True for the Update that flushes the final position immediately before a
    /// TouchEnd in the same frame. Such an update must bypass jitter suppression
    /// so TouchEnd is preceded by the true final position.
    /// </summary>
    public bool IsLiftFlush { get; init; }
}

/// <summary>
/// Kernel-reported state of one MT slot, as read via EVIOCGMTSLOTS after a
/// SYN_DROPPED resync. Coordinates are already-mapped screen coordinates;
/// <see cref="HasPosition"/> is false when the position could not be mapped
/// (uncalibrated axis).
/// </summary>
internal readonly struct SlotResyncState
{
    /// <summary>Tracking id currently occupying the slot; &lt; 0 = empty.</summary>
    public long TrackingId { get; init; }

    public bool HasPosition { get; init; }
    public double X { get; init; }
    public double Y { get; init; }

    public static SlotResyncState Empty => new() { TrackingId = -1 };
}

/// <summary>
/// Evdev multitouch protocol B per-slot state machine.
///
/// Events between SYN_REPORT boundaries are buffered per slot:
/// <c>ABS_MT_SLOT</c> switches the current slot, <c>ABS_MT_TRACKING_ID</c> &gt;= 0
/// begins a contact in the current slot (-1 ends it) and
/// <c>ABS_MT_POSITION_X/Y</c> (already mapped to screen coordinates by the caller)
/// update the current slot. <see cref="CommitFrame"/> is called on SYN_REPORT and
/// yields one change per touched slot.
///
/// Protocol B semantics honored here:
/// - Slot values persist across contacts: the kernel does not resend a coordinate
///   that did not change, even for a brand new contact, so positions are kept
///   after a contact ends.
/// - A Begin is deferred until both coordinates are known (or inherited from the
///   previous contact in the slot); uncalibrated axes simply never deliver a
///   coordinate, so such contacts stay unannounced.
/// - The first contact of a touch session (0 -&gt; 1 active contacts) is the
///   "primary" contact (<see cref="PrimarySlot"/>); primariness is not
///   transferred to surviving contacts so the shared soft cursor is never
///   warped by secondary fingers.
/// </summary>
internal sealed class MultiTouchSlotTracker
{
    /// <summary>
    /// Slot capacity used when EVIOCGABS(ABS_MT_SLOT) does not report a usable
    /// range. The slot array grows on demand if the device addresses more.
    /// </summary>
    public const int FallbackSlotCount = 10;

    private struct Slot
    {
        /// <summary>Tracking id of the contact occupying the slot; -1 = empty.</summary>
        public long ActiveTrackingId;

        /// <summary>True once Begin for <see cref="ActiveTrackingId"/> was committed downstream.</summary>
        public bool Announced;

        /// <summary>Contact awaiting an End at the next commit; -1 = none.</summary>
        public long PendingEndId;
        public bool PendingEndWasAnnounced;

        // Protocol B slot values persist across contacts, so the last known
        // position is kept even after the contact ends.
        public bool HasX, HasY;
        public double X, Y;

        /// <summary>Coordinate changed since the last commit.</summary>
        public bool DirtyX, DirtyY;
    }

    private Slot[] _slots;
    private int _currentSlot;
    private int _activeContacts;
    private int _primarySlot = -1;

    public MultiTouchSlotTracker(int slotCount)
    {
        if (slotCount < 1)
            slotCount = FallbackSlotCount;

        _slots = new Slot[slotCount];
        InitializeSlots(_slots, 0);
    }

    public int SlotCount => _slots.Length;

    public int CurrentSlot => _currentSlot;

    /// <summary>Number of contacts currently down (begun and not yet ended).</summary>
    public int ActiveContactCount => _activeContacts;

    /// <summary>
    /// Slot of the first contact of the current touch session, -1 when none.
    /// Only this contact may drive the shared soft-cursor position.
    /// </summary>
    public int PrimarySlot => _primarySlot;

    /// <summary>
    /// A device that reports MT slots duplicates its first contact through the
    /// legacy single-touch emulation events (ABS_X/ABS_Y/BTN_TOUCH); those must
    /// not synthesize touches or contact 0 would be delivered twice.
    /// </summary>
    public static bool SuppressesSingleTouchEmulation(MultiTouchSlotTracker? tracker)
        => tracker is not null;

    /// <summary>Handles ABS_MT_SLOT: switches the current slot, growing capacity on demand.</summary>
    public void SelectSlot(int slot)
    {
        if (slot < 0)
            return;

        if (slot >= _slots.Length)
            Grow(slot + 1);

        _currentSlot = slot;
    }

    /// <summary>
    /// Handles ABS_MT_TRACKING_ID for the current slot: a value &gt;= 0 begins a
    /// contact, -1 ends the active one. A begin while another contact occupies
    /// the slot (no explicit -1 seen) ends the old contact first.
    /// </summary>
    public void ProcessTrackingId(int value)
    {
        ref var s = ref _slots[_currentSlot];

        if (value >= 0)
        {
            if (s.ActiveTrackingId == value)
                return; // repeated announcement of the same contact

            if (s.ActiveTrackingId >= 0)
                EndActiveContact(ref s);

            s.ActiveTrackingId = value;
            s.Announced = false;
            _activeContacts++;

            if (_activeContacts == 1)
                _primarySlot = _currentSlot;
            else if (_primarySlot == _currentSlot)
                _primarySlot = -1; // slot freed by the primary, reused by a secondary contact
        }
        else if (s.ActiveTrackingId >= 0)
        {
            EndActiveContact(ref s);
        }
    }

    /// <summary>Handles a mapped ABS_MT_POSITION_* targeting the screen X axis for the current slot.</summary>
    public void SetScreenX(double x)
    {
        ref var s = ref _slots[_currentSlot];
        s.X = x;
        s.HasX = true;
        s.DirtyX = true;
    }

    /// <summary>Handles a mapped ABS_MT_POSITION_* targeting the screen Y axis for the current slot.</summary>
    public void SetScreenY(double y)
    {
        ref var s = ref _slots[_currentSlot];
        s.Y = y;
        s.HasY = true;
        s.DirtyY = true;
    }

    /// <summary>
    /// Commits the buffered frame at a SYN_REPORT boundary and returns one change
    /// per touched slot: Begin (new tracking id), Update (moved) or End
    /// (tracking id -1). A contact that began and ended within the same frame is
    /// delivered as a full Begin/End pair so short taps are not lost.
    /// </summary>
    public IReadOnlyList<SlotContactEvent> CommitFrame()
    {
        List<SlotContactEvent>? events = null;

        for (var i = 0; i < _slots.Length; i++)
        {
            ref var s = ref _slots[i];
            var hasPosition = s.HasX && s.HasY;
            var dirty = s.DirtyX || s.DirtyY;

            if (s.PendingEndId >= 0)
            {
                if (hasPosition)
                {
                    if (!s.PendingEndWasAnnounced)
                    {
                        // Begin+End inside one frame: emit the full pair.
                        Add(ref events, SlotContactEventKind.Begin, i, s.PendingEndId, s.X, s.Y);
                    }
                    else if (dirty && s.ActiveTrackingId < 0)
                    {
                        // Flush the final position before TouchEnd (bypasses jitter).
                        Add(ref events, SlotContactEventKind.Update, i, s.PendingEndId, s.X, s.Y, isLiftFlush: true);
                    }

                    Add(ref events, SlotContactEventKind.End, i, s.PendingEndId, s.X, s.Y);
                }

                s.PendingEndId = -1;
                s.PendingEndWasAnnounced = false;
            }

            if (s.ActiveTrackingId >= 0)
            {
                if (!s.Announced)
                {
                    // Begin is deferred until both coordinates are known.
                    if (hasPosition)
                    {
                        s.Announced = true;
                        Add(ref events, SlotContactEventKind.Begin, i, s.ActiveTrackingId, s.X, s.Y);
                    }
                }
                else if (dirty)
                {
                    Add(ref events, SlotContactEventKind.Update, i, s.ActiveTrackingId, s.X, s.Y);
                }
            }

            s.DirtyX = false;
            s.DirtyY = false;
        }

        return (IReadOnlyList<SlotContactEvent>?)events ?? Array.Empty<SlotContactEvent>();
    }

    /// <summary>
    /// SYN_DROPPED resync: returns a Cancel per contact known downstream (announced,
    /// or ended-but-uncommitted) and resets all contact state. Persisted slot
    /// positions are kept — protocol B slot values survive across contacts and the
    /// kernel will not resend coordinates that did not change.
    /// </summary>
    public IReadOnlyList<SlotContactEvent> CancelAll()
    {
        List<SlotContactEvent>? events = null;

        for (var i = 0; i < _slots.Length; i++)
        {
            ref var s = ref _slots[i];

            if (s.Announced)
                Add(ref events, SlotContactEventKind.Cancel, i, s.ActiveTrackingId, s.X, s.Y);
            else if (s.PendingEndId >= 0 && s.PendingEndWasAnnounced)
                Add(ref events, SlotContactEventKind.Cancel, i, s.PendingEndId, s.X, s.Y);

            s.ActiveTrackingId = -1;
            s.Announced = false;
            s.PendingEndId = -1;
            s.PendingEndWasAnnounced = false;
            s.DirtyX = false;
            s.DirtyY = false;
        }

        _activeContacts = 0;
        _primarySlot = -1;

        return (IReadOnlyList<SlotContactEvent>?)events ?? Array.Empty<SlotContactEvent>();
    }

    /// <summary>
    /// Re-seeds the tracker from the kernel per-slot state (EVIOCGMTSLOTS) after
    /// a SYN_DROPPED resync. Must be called right after <see cref="CancelAll"/>.
    /// Contacts that are physically still down (tracking id &gt;= 0) are
    /// re-announced: the returned list contains one Begin per re-seeded contact
    /// whose position is known (from the kernel snapshot, or the persisted slot
    /// position when the snapshot value could not be mapped). Contacts without
    /// any known position are tracked but their Begin is deferred until a
    /// position arrives (standard deferred-begin rule).
    /// </summary>
    public IReadOnlyList<SlotContactEvent> Reseed(SlotResyncState[] kernelSlots)
    {
        List<SlotContactEvent>? events = null;

        var count = Math.Min(kernelSlots.Length, _slots.Length);
        for (var i = 0; i < count; i++)
        {
            var kernel = kernelSlots[i];
            if (kernel.TrackingId < 0)
                continue;

            ref var s = ref _slots[i];

            s.ActiveTrackingId = kernel.TrackingId;
            _activeContacts++;
            if (_primarySlot < 0)
                _primarySlot = i;

            if (kernel.HasPosition)
            {
                s.X = kernel.X;
                s.Y = kernel.Y;
                s.HasX = true;
                s.HasY = true;
            }

            // Announce immediately when a position is known (kernel snapshot or
            // the persisted protocol B slot value); otherwise the Begin stays
            // deferred and commits with the first position update.
            if (s.HasX && s.HasY)
            {
                s.Announced = true;
                Add(ref events, SlotContactEventKind.Begin, i, s.ActiveTrackingId, s.X, s.Y);
            }

            s.DirtyX = false;
            s.DirtyY = false;
        }

        return (IReadOnlyList<SlotContactEvent>?)events ?? Array.Empty<SlotContactEvent>();
    }

    private void EndActiveContact(ref Slot s)
    {
        // Note: if two contacts end in the same slot within one frame (extremely
        // unlikely), only the latest End survives until the commit.
        s.PendingEndId = s.ActiveTrackingId;
        s.PendingEndWasAnnounced = s.Announced;
        s.ActiveTrackingId = -1;
        s.Announced = false;
        _activeContacts--;
    }

    private void Grow(int needed)
    {
        var newLength = Math.Max(needed, _slots.Length * 2);
        var slots = new Slot[newLength];
        Array.Copy(_slots, slots, _slots.Length);
        InitializeSlots(slots, _slots.Length);
        _slots = slots;
    }

    private static void InitializeSlots(Slot[] slots, int from)
    {
        for (var i = from; i < slots.Length; i++)
        {
            slots[i].ActiveTrackingId = -1;
            slots[i].PendingEndId = -1;
        }
    }

    private static void Add(
        ref List<SlotContactEvent>? events,
        SlotContactEventKind kind,
        int slotIndex,
        long trackingId,
        double x,
        double y,
        bool isLiftFlush = false)
    {
        events ??= new List<SlotContactEvent>();
        events.Add(new SlotContactEvent
        {
            Kind = kind,
            SlotIndex = slotIndex,
            TrackingId = trackingId,
            X = x,
            Y = y,
            IsLiftFlush = isLiftFlush
        });
    }
}
