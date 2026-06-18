using BuildingBlocks.Domain.Enumerations;

namespace Identity.Domain.Enumerations;

public sealed class UserType : Enumeration
{
    public static readonly UserType SystemAdmin = new(1, nameof(SystemAdmin));
    public static readonly UserType Employee    = new(2, nameof(Employee));
    public static readonly UserType Customer    = new(3, nameof(Customer));

    private UserType(int id, string name) : base(id, name) { }
}
