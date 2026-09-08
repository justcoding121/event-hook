namespace EventHook
{
    /// <summary>
    /// Why <see cref="HookStartResult"/> failed (or <see cref="None"/> on success).
    /// </summary>
    public enum HookFailureReason
    {
        None = 0,
        PermissionDenied = 1,
        PrivilegeRequired = 2,
        DisplayUnavailable = 3,
        NotSupportedOnPlatform = 4,
        AlreadyInUse = 5,
        NativeFailure = 6
    }
}
