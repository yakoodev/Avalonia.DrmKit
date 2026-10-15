using Avalonia.DrmKit.Input;
using Avalonia.Threading;

namespace Avalonia.DrmKit
{
    /// <summary>
    /// Программный курсор DrmKit во время работы: на сенсорном киоске его можно выключить, на
    /// приборе с мышью — оставить. Начальное значение задают <see cref="SoftCursorOptions.Enabled"/>
    /// и переменная окружения <c>DRMKIT_SOFT_CURSOR</c> (0/false — выключить).
    /// </summary>
    public static class DrmSoftCursor
    {
        /// <summary>Показывать ли программный курсор. Можно менять с любого потока.</summary>
        public static bool IsEnabled
        {
            get => SoftCursorHub.IsEnabled;
            set
            {
                if (Dispatcher.UIThread.CheckAccess())
                    SoftCursorHub.SetEnabled(value);
                else
                    Dispatcher.UIThread.Post(() => SoftCursorHub.SetEnabled(value));
            }
        }

        internal static bool ResolveInitial(bool optionsEnabled, string? env) =>
            env?.Trim().ToLowerInvariant() switch
            {
                "0" or "false" or "off" or "no" => false,
                "1" or "true" or "on" or "yes" => true,
                _ => optionsEnabled,
            };
    }
}
