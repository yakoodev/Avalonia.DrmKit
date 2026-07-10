using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input.Raw;

namespace Avalonia.DrmKit.Input;

/// <summary>
/// Fan-out point between the DRM input backend and the per-TopLevel touch
/// feedback overlays (mirrors <see cref="SoftCursorHub"/>). Also owns the
/// "any contact active" state used to hide the soft cursor while touching.
/// <see cref="Notify"/> is always invoked on the UI thread (from the backend's
/// dispatched delivery), so no locking is needed for the contact set.
/// </summary>
internal static class TouchFeedbackHub
{
    private static readonly ConcurrentDictionary<TopLevel, TouchFeedbackLayer> _layers = new();

    // UI-thread only.
    private static readonly HashSet<long> _activeContacts = new();
    private static bool _hideCursorWhileTouching;

    public static void Configure(bool hideCursorWhileTouching)
    {
        _hideCursorWhileTouching = hideCursorWhileTouching;
    }

    public static void Register(TopLevel tl, TouchFeedbackLayer layer)
    {
        _layers[tl] = layer;
    }

    /// <summary>
    /// Delivers one raw touch event to every registered overlay and maintains
    /// soft-cursor suppression while any contact is active. UI thread only.
    /// </summary>
    public static void Notify(long contactId, RawPointerEventType type, Point position)
    {
        if (_hideCursorWhileTouching)
        {
            var wasActive = _activeContacts.Count > 0;

            switch (type)
            {
                case RawPointerEventType.TouchBegin:
                    _activeContacts.Add(contactId);
                    break;
                case RawPointerEventType.TouchEnd:
                case RawPointerEventType.TouchCancel:
                    _activeContacts.Remove(contactId);
                    break;
            }

            var isActive = _activeContacts.Count > 0;
            if (isActive != wasActive)
                SoftCursorHub.SetTouchSuppressed(isActive);
        }

        foreach (var kv in _layers)
        {
            try { kv.Value.OnTouch(contactId, type, position); } catch { }
        }
    }
}
