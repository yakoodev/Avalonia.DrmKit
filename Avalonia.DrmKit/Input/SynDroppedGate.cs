namespace Avalonia.DrmKit.Input;

/// <summary>
/// Action decided by <see cref="SynDroppedGate"/> for an incoming evdev event.
/// </summary>
internal enum SynGateAction
{
    /// <summary>Process the event normally.</summary>
    Process,

    /// <summary>Discard the event (resync in progress, or an ignored EV_SYN code).</summary>
    Drop,

    /// <summary>
    /// SYN_DROPPED was just observed: discard the event and reset all pending
    /// per-device frame state before resuming.
    /// </summary>
    EnterDropped
}

/// <summary>
/// Standard evdev SYN_DROPPED resync state machine (per device).
/// When the kernel drops events (EV_SYN/SYN_DROPPED), the packet in progress is
/// incomplete: everything up to and including the next SYN_REPORT must be
/// discarded so stale half-frames do not commit as jumped touches.
/// </summary>
internal sealed class SynDroppedGate
{
    private const ushort EvSyn = 0x00;
    private const ushort SynReport = 0x00;
    private const ushort SynDropped = 0x03;

    public bool Dropping { get; private set; }

    public SynGateAction Process(ushort type, ushort code)
    {
        if (type == EvSyn)
        {
            if (code == SynDropped)
            {
                var wasDropping = Dropping;
                Dropping = true;
                return wasDropping ? SynGateAction.Drop : SynGateAction.EnterDropped;
            }

            if (code == SynReport)
            {
                if (Dropping)
                {
                    // This SYN_REPORT closes the partial packet; consume it and resume
                    // with the next full packet.
                    Dropping = false;
                    return SynGateAction.Drop;
                }

                return SynGateAction.Process;
            }

            // Other SYN codes (e.g. SYN_MT_REPORT) are not frame boundaries for this backend.
            return SynGateAction.Drop;
        }

        return Dropping ? SynGateAction.Drop : SynGateAction.Process;
    }
}
