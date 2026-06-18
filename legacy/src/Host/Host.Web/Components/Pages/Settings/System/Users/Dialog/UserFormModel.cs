using Identity.Application.Commands.InviteUser;
using Identity.Application.Commands.UpdateUser;
using Identity.Contracts.Features.User;

namespace Host.Web.Components.Pages.Settings.System.Users.Dialog;

/// <summary>
/// UI state for User Add (=invite) / Update dialogs. Add maps to <see cref="InviteUserCommand"/>
/// because new users always start in <c>PendingActivation</c> — we never set passwords from the
/// admin UI (matches Identity's onboarding flow). Update maps to <see cref="UpdateUserCommand"/>.
/// </summary>
public sealed class UserFormModel
{
    public Guid? Id { get; set; }

    public string Username { get; set; } = "";

    public string Email { get; set; } = "";

    /// <summary>1=SystemAdmin, 2=Employee, 3=Customer (Identity.Domain.Enumerations.UserType ids).</summary>
    public int UserTypeId { get; set; } = 2;

    public bool IsActive { get; set; } = true;

    public List<Guid> RoleIds { get; set; } = [];

    /// <summary>Read-only display fields (Update mode only).</summary>
    public string StatusDisplay { get; set; } = "";

    public string UserTypeDisplay { get; set; } = "";

    public bool HasPendingInvitation { get; set; }

    public bool IsLocked { get; set; }

    public DateTime? InvitationExpiresAt { get; set; }

    /// <summary>Populated when invitation is pending and token is still valid (not expired).</summary>
    public string? InvitationToken { get; set; }

    public DateTime? CreatedAt { get; set; }

    public Guid? ExternalReferenceId { get; set; }

    public static UserFormModel FromDto(UserDto dto) =>
        new()
        {
            Id = dto.Id,
            Username = dto.Username,
            Email = dto.Email,
            UserTypeId = dto.UserType switch
            {
                "SystemAdmin" => 1,
                "Customer" => 3,
                _ => 2
            },
            IsActive = dto.IsActive,
            RoleIds = dto.Roles.Select(r => r.RoleId).ToList(),
            StatusDisplay = dto.Status,
            UserTypeDisplay = dto.UserType,
            HasPendingInvitation = dto.HasPendingInvitation,
            IsLocked = dto.IsLocked,
            InvitationExpiresAt = dto.InvitationExpiresAt,
            InvitationToken = dto.InvitationToken,
            CreatedAt = dto.CreatedAt,
            ExternalReferenceId = dto.ExternalReferenceId
        };

    public UserFormModel Clone() =>
        new()
        {
            Id = Id,
            Username = Username,
            Email = Email,
            UserTypeId = UserTypeId,
            IsActive = IsActive,
            RoleIds = RoleIds.ToList(),
            StatusDisplay = StatusDisplay,
            UserTypeDisplay = UserTypeDisplay,
            HasPendingInvitation = HasPendingInvitation,
            IsLocked = IsLocked,
            InvitationExpiresAt = InvitationExpiresAt,
            InvitationToken = InvitationToken,
            CreatedAt = CreatedAt,
            ExternalReferenceId = ExternalReferenceId
        };

    public InviteUserCommand ToInviteUserCommand() =>
        new(
            Username.Trim(),
            Email.Trim(),
            UserTypeId,
            ExternalReferenceId: null,
            RoleIds: RoleIds.ToList());

    public UpdateUserCommand ToUpdateUserCommand(Guid id) =>
        new(
            id,
            Email.Trim(),
            IsActive,
            RoleIds.ToList());
}
