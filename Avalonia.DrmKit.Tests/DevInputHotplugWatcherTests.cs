using Avalonia.DrmKit.Input;
using Xunit;

namespace Avalonia.DrmKit.Tests;

public class DevInputHotplugWatcherTests
{
    /// <summary>
    /// Builds one struct inotify_event record: int wd, uint mask, uint cookie,
    /// uint len, then a NUL-padded name of exactly len bytes.
    /// </summary>
    private static byte[] Record(int wd, uint mask, string name, int paddedLen)
    {
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
        Assert.True(paddedLen >= nameBytes.Length);

        var record = new byte[16 + paddedLen];
        BitConverter.GetBytes(wd).CopyTo(record, 0);
        BitConverter.GetBytes(mask).CopyTo(record, 4);
        BitConverter.GetBytes(0u).CopyTo(record, 8);
        BitConverter.GetBytes((uint)paddedLen).CopyTo(record, 12);
        nameBytes.CopyTo(record, 16);
        return record;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var total = parts.Sum(p => p.Length);
        var buffer = new byte[total];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(buffer, offset);
            offset += part.Length;
        }

        return buffer;
    }

    [Fact]
    public void Parses_single_event_with_nul_padded_name()
    {
        var buffer = Record(1, DevInputHotplugWatcher.IN_CREATE, "event5", 16);

        var events = DevInputHotplugWatcher.ParseEventBuffer(buffer, buffer.Length).ToList();

        var e = Assert.Single(events);
        Assert.Equal(DevInputHotplugWatcher.IN_CREATE, e.Mask);
        Assert.Equal("event5", e.Name);
    }

    [Fact]
    public void Parses_multiple_events_from_one_read_buffer()
    {
        var buffer = Concat(
            Record(1, DevInputHotplugWatcher.IN_CREATE, "event7", 16),
            Record(1, DevInputHotplugWatcher.IN_ATTRIB, "event7", 16),
            Record(1, DevInputHotplugWatcher.IN_DELETE, "event3", 8));

        var events = DevInputHotplugWatcher.ParseEventBuffer(buffer, buffer.Length).ToList();

        Assert.Equal(3, events.Count);
        Assert.Equal((DevInputHotplugWatcher.IN_CREATE, "event7"), events[0]);
        Assert.Equal((DevInputHotplugWatcher.IN_ATTRIB, "event7"), events[1]);
        Assert.Equal((DevInputHotplugWatcher.IN_DELETE, "event3"), events[2]);
    }

    [Fact]
    public void Event_without_name_yields_empty_name()
    {
        var buffer = Record(1, DevInputHotplugWatcher.IN_ATTRIB, string.Empty, 0);

        var events = DevInputHotplugWatcher.ParseEventBuffer(buffer, buffer.Length).ToList();

        var e = Assert.Single(events);
        Assert.Equal(string.Empty, e.Name);
    }

    [Fact]
    public void Truncated_record_stops_parsing_without_throwing()
    {
        var full = Record(1, DevInputHotplugWatcher.IN_CREATE, "event1", 16);
        var truncated = Concat(full, Record(1, DevInputHotplugWatcher.IN_CREATE, "event2", 16));

        // Claim a length that cuts into the second record's name.
        var events = DevInputHotplugWatcher.ParseEventBuffer(truncated, full.Length + 20).ToList();

        var e = Assert.Single(events);
        Assert.Equal("event1", e.Name);
    }

    [Theory]
    [InlineData("event0", true)]
    [InlineData("event12", true)]
    [InlineData("event", false)]
    [InlineData("mouse0", false)]
    [InlineData("mice", false)]
    [InlineData("js0", false)]
    [InlineData("by-id", false)]
    public void Only_event_nodes_are_recognized(string name, bool expected)
    {
        Assert.Equal(expected, DevInputHotplugWatcher.IsEventDeviceName(name));
    }
}
