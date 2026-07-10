using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Avalonia.DrmKit.Input;

/// <summary>
/// Watches /dev/input with inotify from a single background thread and reports
/// appearing/disappearing event* device nodes, enabling touchscreen/keyboard/
/// mouse hotplug for the DRM input backend.
///
/// IN_CREATE and IN_ATTRIB both map to "appeared" — a freshly created node often
/// gets its access mode fixed by udev a moment later, so the ATTRIB retriggers
/// an open attempt that failed on permissions. IN_DELETE maps to "removed".
///
/// If inotify cannot be initialized the failure is logged once and the backend
/// behaves as before (startup device set only).
/// </summary>
internal sealed class DevInputHotplugWatcher
{
    private const int IN_NONBLOCK = 0x800;
    private const int IN_CLOEXEC = 0x80000;

    internal const uint IN_ATTRIB = 0x004;
    internal const uint IN_CREATE = 0x100;
    internal const uint IN_DELETE = 0x200;

    private const short POLLIN = 0x001;
    private const int PollTimeoutMs = 500;
    private const int EventHeaderSize = 16; // wd + mask + cookie + len

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int fd;
        public short events;
        public short revents;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int inotify_init1(int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int inotify_add_watch(int fd, string pathname, uint mask);

    [DllImport("libc", SetLastError = true)]
    private static extern IntPtr read(int fd, byte[] buffer, UIntPtr count);

    [DllImport("libc")]
    private static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int poll([In, Out] PollFd[] fds, uint nfds, int timeout);

    private readonly string _directory;
    private readonly Action<string> _onDeviceAppeared;
    private readonly Action<string> _onDeviceRemoved;
    private readonly Func<bool> _keepRunning;

    private int _fd = -1;

    public DevInputHotplugWatcher(
        string directory,
        Action<string> onDeviceAppeared,
        Action<string> onDeviceRemoved,
        Func<bool> keepRunning)
    {
        _directory = directory;
        _onDeviceAppeared = onDeviceAppeared;
        _onDeviceRemoved = onDeviceRemoved;
        _keepRunning = keepRunning;
    }

    /// <summary>
    /// Initializes inotify and starts the watcher thread. Returns false (after
    /// logging once) when inotify is unavailable; the caller then simply keeps
    /// the startup device set.
    /// </summary>
    public bool TryStart()
    {
        try
        {
            _fd = inotify_init1(IN_NONBLOCK | IN_CLOEXEC);
            if (_fd < 0)
            {
                Console.Error.WriteLine(
                    $"[DrmKit][input] inotify_init1 failed (errno {Marshal.GetLastWin32Error()}); input hotplug disabled.");
                return false;
            }

            var wd = inotify_add_watch(_fd, _directory, IN_CREATE | IN_DELETE | IN_ATTRIB);
            if (wd < 0)
            {
                Console.Error.WriteLine(
                    $"[DrmKit][input] inotify_add_watch({_directory}) failed (errno {Marshal.GetLastWin32Error()}); input hotplug disabled.");
                close(_fd);
                _fd = -1;
                return false;
            }

            var thread = new Thread(WatchLoop)
            {
                IsBackground = true,
                Name = "DrmKit.InputHotplug"
            };
            thread.Start();
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[DrmKit][input] Input hotplug watcher failed to start: {ex.GetType().Name}: {ex.Message}; input hotplug disabled.");
            if (_fd >= 0)
            {
                try { close(_fd); } catch { }
                _fd = -1;
            }

            return false;
        }
    }

    private void WatchLoop()
    {
        var buffer = new byte[4096];
        var fds = new PollFd[1];

        try
        {
            while (_keepRunning())
            {
                fds[0].fd = _fd;
                fds[0].events = POLLIN;
                fds[0].revents = 0;

                var ready = poll(fds, 1, PollTimeoutMs);
                if (ready < 0)
                {
                    var errno = Marshal.GetLastWin32Error();
                    if (errno == 4) // EINTR
                        continue;
                    break;
                }

                if (ready == 0 || (fds[0].revents & POLLIN) == 0)
                    continue;

                var length = (long)read(_fd, buffer, (UIntPtr)buffer.Length);
                if (length <= 0)
                    continue; // EAGAIN (non-blocking) or transient error

                foreach (var (mask, name) in ParseEventBuffer(buffer, (int)length))
                {
                    if (!IsEventDeviceName(name))
                        continue;

                    var path = _directory + "/" + name;

                    if ((mask & IN_DELETE) != 0)
                        SafeInvoke(_onDeviceRemoved, path);
                    else if ((mask & (IN_CREATE | IN_ATTRIB)) != 0)
                        SafeInvoke(_onDeviceAppeared, path);
                }
            }
        }
        finally
        {
            try { close(_fd); } catch { }
            _fd = -1;
        }
    }

    private static void SafeInvoke(Action<string> action, string path)
    {
        try { action(path); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DrmKit][input] Hotplug handler failed for {path}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    internal static bool IsEventDeviceName(string name)
        => name.StartsWith("event", StringComparison.Ordinal) && name.Length > 5;

    /// <summary>
    /// Parses a raw inotify read buffer into (mask, name) pairs. Each record is
    /// struct inotify_event { int wd; uint32 mask; uint32 cookie; uint32 len; char name[len]; }
    /// where name is NUL-padded to len bytes. Pure and unit-testable.
    /// </summary>
    internal static IEnumerable<(uint Mask, string Name)> ParseEventBuffer(byte[] buffer, int length)
    {
        var offset = 0;

        while (offset + EventHeaderSize <= length)
        {
            var mask = BitConverter.ToUInt32(buffer, offset + 4);
            var nameLen = (int)BitConverter.ToUInt32(buffer, offset + 12);

            if (nameLen < 0 || offset + EventHeaderSize + nameLen > length)
                yield break; // malformed/truncated record

            var name = string.Empty;
            if (nameLen > 0)
            {
                var nameStart = offset + EventHeaderSize;
                var nul = Array.IndexOf(buffer, (byte)0, nameStart, nameLen);
                var effectiveLen = nul >= 0 ? nul - nameStart : nameLen;
                name = Encoding.UTF8.GetString(buffer, nameStart, effectiveLen);
            }

            yield return (mask, name);

            offset += EventHeaderSize + nameLen;
        }
    }
}
