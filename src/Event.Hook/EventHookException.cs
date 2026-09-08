using System;

namespace EventHook
{
    /// <summary>
    /// Thrown by <see cref="HookStartResult.ThrowIfFailed"/> when hook setup failed.
    /// </summary>
    public sealed class EventHookException : Exception
    {
        public EventHookException(HookFailureReason reason, string message)
            : base(message)
        {
            Reason = reason;
        }

        public HookFailureReason Reason { get; }
    }
}
