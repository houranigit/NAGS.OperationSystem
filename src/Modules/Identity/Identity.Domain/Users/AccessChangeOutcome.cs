namespace Identity.Domain.Users;

/// <summary>Whether an access-change request altered the persisted role or account type.</summary>
public enum AccessChangeOutcome
{
    Unchanged = 0,
    Changed = 1
}
