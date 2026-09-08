namespace EventHook
{
    /// <summary>
    /// Result of starting a watcher or registering a hotkey.
    /// </summary>
    public readonly struct HookStartResult
    {
        private HookStartResult(bool success, HookFailureReason reason, string message)
        {
            Success = success;
            Reason = reason;
            Message = message ?? string.Empty;
        }

        public bool Success { get; }

        public HookFailureReason Reason { get; }

        public string Message { get; }

        public static HookStartResult Ok() =>
            new HookStartResult(true, HookFailureReason.None, string.Empty);

        public static HookStartResult Fail(HookFailureReason reason, string message) =>
            new HookStartResult(false, reason, message);

        public void ThrowIfFailed()
        {
            if (!Success)
            {
                throw new EventHookException(Reason, Message);
            }
        }

        public override string ToString() =>
            Success ? "Ok" : $"{Reason}: {Message}";
    }
}
