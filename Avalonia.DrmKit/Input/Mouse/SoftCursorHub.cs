using Avalonia;
using Avalonia.Controls;
using System;
using System.Collections.Concurrent;

namespace Avalonia.DrmKit.Input
{
    internal static class SoftCursorHub
    {
        private static readonly ConcurrentDictionary<TopLevel, SoftCursorLayer> _layers = new();
        private static volatile bool _enabled = true;

        /// <summary>Курсор включён (см. <see cref="DrmSoftCursor.IsEnabled"/>).</summary>
        public static bool IsEnabled => _enabled;

        public static void Register(TopLevel tl, SoftCursorLayer layer)
        {
            _layers[tl] = layer;
            layer.SetDisabled(!_enabled);
        }

        /// <summary>Включить/выключить курсор на всех окнах. Вызывать на UI-потоке.</summary>
        public static void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            foreach (var kv in _layers)
            {
                try { kv.Value.SetDisabled(!enabled); } catch { }
            }
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
