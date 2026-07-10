using Avalonia;
using Avalonia.Controls;
using System;
using System.Collections.Concurrent;

namespace Avalonia.DrmKit.Input
{
    internal static class SoftCursorHub
    {
        private static readonly ConcurrentDictionary<TopLevel, SoftCursorLayer> _layers = new();

        public static void Register(TopLevel tl, SoftCursorLayer layer)
        {
            _layers[tl] = layer;
        }

        public static void UpdatePosition(Point p)
        {
            foreach (var kv in _layers)
            {
                try { kv.Value.UpdatePosition(p); } catch { }
            }
        }

        public static void SetPressed(bool pressed)
        {
            foreach (var kv in _layers)
            {
                try { kv.Value.SetPressed(pressed); } catch { }
            }
        }

        /// <summary>
        /// Hides/releases every soft cursor while a touch contact is active
        /// (see <see cref="SoftCursorLayer.SetTouchSuppressed"/>).
        /// </summary>
        public static void SetTouchSuppressed(bool suppressed)
        {
            foreach (var kv in _layers)
            {
                try { kv.Value.SetTouchSuppressed(suppressed); } catch { }
            }
        }
    }
}
