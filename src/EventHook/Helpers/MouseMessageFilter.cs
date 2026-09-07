using EventHook.Hooks;

namespace EventHook.Helpers
{
    /// <summary>
    /// Helpers for filtering low-level mouse messages.
    /// </summary>
    public static class MouseMessageFilter
    {
        /// <summary>
        /// Returns false when <paramref name="includeMouseMove"/> is false and the message is WM_MOUSEMOVE.
        /// </summary>
        public static bool ShouldRaise(MouseMessages message, bool includeMouseMove)
        {
            if (!includeMouseMove && message == MouseMessages.WM_MOUSEMOVE)
            {
                return false;
            }

            return true;
        }
    }
}
