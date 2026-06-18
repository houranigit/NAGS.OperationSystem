namespace Identity.Domain.Policies;

public sealed class PasswordPolicy
{
    public int HistoryCount { get; }
    public int? ExpiryDays { get; }

    public static readonly PasswordPolicy Default = new(5, 90);

    public PasswordPolicy(int historyCount, int? expiryDays)
    {
        if (historyCount < 0)
            throw new ArgumentException("HistoryCount must be non-negative.", nameof(historyCount));
        if (expiryDays.HasValue && expiryDays.Value <= 0)
            throw new ArgumentException("ExpiryDays must be positive.", nameof(expiryDays));

        HistoryCount = historyCount;
        ExpiryDays = expiryDays;
    }
}
