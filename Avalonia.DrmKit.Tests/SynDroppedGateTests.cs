using Avalonia.DrmKit.Input;
using Xunit;

namespace Avalonia.DrmKit.Tests;

public class SynDroppedGateTests
{
    private const ushort EvSyn = 0x00;
    private const ushort EvKey = 0x01;
    private const ushort EvAbs = 0x03;
    private const ushort SynReport = 0x00;
    private const ushort SynDropped = 0x03;
    private const ushort SynMtReport = 0x02;

    [Fact]
    public void Normal_flow_processes_everything()
    {
        var gate = new SynDroppedGate();

        Assert.Equal(SynGateAction.Process, gate.Process(EvAbs, 0x35));
        Assert.Equal(SynGateAction.Process, gate.Process(EvKey, 0x14a));
        Assert.Equal(SynGateAction.Process, gate.Process(EvSyn, SynReport));
        Assert.False(gate.Dropping);
    }

    [Fact]
    public void Syn_dropped_enters_resync_once()
    {
        var gate = new SynDroppedGate();

        Assert.Equal(SynGateAction.EnterDropped, gate.Process(EvSyn, SynDropped));
        Assert.True(gate.Dropping);

        // A second SYN_DROPPED while already resyncing is just dropped.
        Assert.Equal(SynGateAction.Drop, gate.Process(EvSyn, SynDropped));
        Assert.True(gate.Dropping);
    }

    [Fact]
    public void Events_after_syn_dropped_are_discarded_until_next_syn_report()
    {
        var gate = new SynDroppedGate();

        gate.Process(EvSyn, SynDropped);

        // Remainder of the partial packet: dropped.
        Assert.Equal(SynGateAction.Drop, gate.Process(EvAbs, 0x35));
        Assert.Equal(SynGateAction.Drop, gate.Process(EvAbs, 0x36));
        Assert.Equal(SynGateAction.Drop, gate.Process(EvKey, 0x14a));

        // The SYN_REPORT closing the partial packet is consumed, not processed.
        Assert.Equal(SynGateAction.Drop, gate.Process(EvSyn, SynReport));
        Assert.False(gate.Dropping);

        // The next full packet is processed normally.
        Assert.Equal(SynGateAction.Process, gate.Process(EvAbs, 0x35));
        Assert.Equal(SynGateAction.Process, gate.Process(EvSyn, SynReport));
    }

    [Fact]
    public void Non_report_syn_codes_are_not_frame_boundaries()
    {
        var gate = new SynDroppedGate();

        // SYN_MT_REPORT outside resync is ignored (not processed as a frame commit).
        Assert.Equal(SynGateAction.Drop, gate.Process(EvSyn, SynMtReport));
        Assert.False(gate.Dropping);

        // During resync, SYN_MT_REPORT must not terminate the drop window.
        gate.Process(EvSyn, SynDropped);
        Assert.Equal(SynGateAction.Drop, gate.Process(EvSyn, SynMtReport));
        Assert.True(gate.Dropping);
    }

    [Fact]
    public void Resync_can_repeat()
    {
        var gate = new SynDroppedGate();

        gate.Process(EvSyn, SynDropped);
        gate.Process(EvSyn, SynReport);
        Assert.Equal(SynGateAction.Process, gate.Process(EvAbs, 0x35));

        Assert.Equal(SynGateAction.EnterDropped, gate.Process(EvSyn, SynDropped));
        Assert.Equal(SynGateAction.Drop, gate.Process(EvAbs, 0x36));
        Assert.Equal(SynGateAction.Drop, gate.Process(EvSyn, SynReport));
        Assert.Equal(SynGateAction.Process, gate.Process(EvAbs, 0x35));
    }
}
