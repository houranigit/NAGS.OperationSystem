using BuildingBlocks.Domain.Enumerations;

namespace Identity.Domain.Enumerations;

public sealed class UserStatus : Enumeration
{
    public static readonly UserStatus PendingActivation = new(1, nameof(PendingActivation));
    public static readonly UserStatus Active            = new(2, nameof(Active));
    public static readonly UserStatus Locked            = new(3, nameof(Locked));
    public static readonly UserStatus PasswordExpired   = new(4, nameof(PasswordExpired));
    public static readonly UserStatus Deactivated       = new(5, nameof(Deactivated));

    private UserStatus(int id, string name) : base(id, name) { }
}
