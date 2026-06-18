namespace Identity.Domain.Policies;

public sealed class LockoutPolicy
{
    public int MaxFailedAttempts { get; }
    public TimeSpan LockoutDuration { get; }

    public static readonly LockoutPolicy Default = new(5, TimeSpan.FromMinutes(15));

    public LockoutPolicy(int maxFailedAttempts, TimeSpan lockoutDuration)
    {
        if (maxFailedAttempts <= 0)
            throw new ArgumentException("MaxFailedAttempts must be greater than 0.", nameof(maxFailedAttempts));
        if (lockoutDuration <= TimeSpan.Zero)
            throw new ArgumentException("LockoutDuration must be positive.", nameof(lockoutDuration));

        MaxFailedAttempts = maxFailedAttempts;
        LockoutDuration = lockoutDuration;
    }
}
