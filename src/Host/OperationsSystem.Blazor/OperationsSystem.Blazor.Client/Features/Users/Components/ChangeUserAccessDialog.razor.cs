using Microsoft.AspNetCore.Components;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Auth;
using OperationsSystem.Blazor.Client.Localization;
using Radzen;

namespace OperationsSystem.Blazor.Client.Features.Users.Components;

public partial class ChangeUserAccessDialog : IAsyncDisposable
{
    private readonly AccessChangeModel model = new();
    private readonly CancellationTokenSource disposeCts = new();
    private IReadOnlyList<RoleOption> roles = [];
    private IReadOnlyList<UserTypeOption> targetTypeOptions = [];
    private CancellationTokenSource? roleLoadCts;
    private string targetUserType = string.Empty;
    private string? loadErrorMessage;
    private string? errorMessage;
    private bool isLoadingRoles;
    private bool isConfirming;
    private bool isSubmitting;
    private bool isStale;
    private bool accountTypeWasChanged;
    private int roleLoadGeneration;

    [Inject] private IdentityApiClient Identity { get; set; } = null!;
    [Inject] private DialogService DialogService { get; set; } = null!;
    [Inject] private AuthSession Auth { get; set; } = null!;

    [Parameter, EditorRequired] public Guid UserId { get; set; }
    [Parameter, EditorRequired] public string DisplayName { get; set; } = string.Empty;
    [Parameter, EditorRequired] public Guid CurrentRoleId { get; set; }
    [Parameter, EditorRequired] public string CurrentRoleName { get; set; } = string.Empty;
    [Parameter, EditorRequired] public string CurrentUserType { get; set; } = string.Empty;
    [Parameter, EditorRequired] public string RowVersion { get; set; } = string.Empty;

    private bool CanSelectAccountType => targetTypeOptions.Count > 1;
    private bool IsBusy => isConfirming || isSubmitting;

    private RoleOption? SelectedRole =>
        model.RoleId is { } id
            ? roles.FirstOrDefault(role => role.Id == id)
            : null;

    private bool HasPendingChange =>
        SelectedRole is not null
        && UserAccessChangePolicy.RequiresConfirmation(
            CurrentUserType,
            CurrentRoleId,
            targetUserType,
            model.RoleId,
            Auth.HasPermission(IdentityPermissions.UsersChangeAccountType));

    private bool CanSubmit =>
        !IsBusy
        && !isLoadingRoles
        && !isStale
        && loadErrorMessage is null
        && HasPendingChange;

    private UserAccessChangeKind ChangeKind =>
        UserAccessChangePolicy.Classify(CurrentUserType, targetUserType);

    private string CurrentUserTypeLabel => UserTypeLabel(CurrentUserType);
    private string TargetUserTypeLabel => UserTypeLabel(targetUserType);
    private string ReviewArrowClass =>
        UiText.IsArabic ? "cua-review-arrow cua-review-arrow--rtl" : "cua-review-arrow";

    private string AccountTypeRestrictionMessage =>
        UserAccessChangePolicy.IsDirectType(CurrentUserType)
            ? UiStrings.Users.ChangeAccountTypePermissionRequired
            : UiStrings.Users.LinkedAccountTypeImmutable;

    protected override async Task OnInitializedAsync()
    {
        targetUserType = CurrentUserType;
        targetTypeOptions = UserAccessChangePolicy
            .AllowedTargetTypes(
                CurrentUserType,
                Auth.HasPermission(IdentityPermissions.UsersChangeAccountType))
            .Select(type => new UserTypeOption(type, UserTypeLabel(type)))
            .ToArray();

        await LoadRolesAsync(selectCurrentRole: true);
    }

    private async Task OnTargetTypeChangedAsync(string? userType)
    {
        if (IsBusy
            || string.IsNullOrWhiteSpace(userType)
            || string.Equals(targetUserType, userType, StringComparison.Ordinal)
            || !targetTypeOptions.Any(option => string.Equals(option.Value, userType, StringComparison.Ordinal)))
        {
            return;
        }

        targetUserType = userType;
        accountTypeWasChanged = true;
        model.RoleId = null;
        errorMessage = null;
        await LoadRolesAsync(selectCurrentRole: false);
    }

    private Task RetryRolesAsync()
    {
        if (IsBusy)
            return Task.CompletedTask;

        errorMessage = null;
        return LoadRolesAsync(
            selectCurrentRole:
                !accountTypeWasChanged
                && string.Equals(targetUserType, CurrentUserType, StringComparison.Ordinal));
    }

    private async Task LoadRolesAsync(bool selectCurrentRole)
    {
        roleLoadCts?.Cancel();
        roleLoadCts?.Dispose();

        var requestCts = CancellationTokenSource.CreateLinkedTokenSource(disposeCts.Token);
        roleLoadCts = requestCts;
        var generation = ++roleLoadGeneration;

        isLoadingRoles = true;
        loadErrorMessage = null;
        roles = [];
        model.RoleId = null;

        try
        {
            var loaded = await Identity.GetRoleOptionsAsync(
                targetUserType,
                assignableOnly: true,
                requestCts.Token);

            if (!UserAccessChangePolicy.ShouldAcceptRoleLoadResult(
                    generation,
                    roleLoadGeneration,
                    requestCts.IsCancellationRequested))
            {
                return;
            }

            roles = loaded
                .Where(role => string.Equals(
                    role.CompatibleUserType,
                    targetUserType,
                    StringComparison.Ordinal))
                .ToArray();

            if (selectCurrentRole && roles.Any(role => role.Id == CurrentRoleId))
                model.RoleId = CurrentRoleId;
        }
        catch (OperationCanceledException) when (requestCts.IsCancellationRequested)
        {
            // A newer account-type selection or dialog disposal superseded this request.
        }
        catch (ApiException ex)
        {
            // Some transports surface an aborted request as their API error rather than
            // OperationCanceledException. Superseded requests must never escape the event handler
            // or overwrite the latest selection's state.
            if (UserAccessChangePolicy.ShouldAcceptRoleLoadResult(
                    generation,
                    roleLoadGeneration,
                    requestCts.IsCancellationRequested))
            {
                loadErrorMessage = ex.ToDisplayMessage(UiStrings.Errors.LoadFailed);
            }
        }
        finally
        {
            if (UserAccessChangePolicy.ShouldAcceptRoleLoadResult(
                    generation,
                    roleLoadGeneration,
                    requestCts.IsCancellationRequested))
            {
                isLoadingRoles = false;
            }
        }
    }

    private async Task SubmitAsync()
    {
        errorMessage = null;

        // The form event can be raised more than once before a confirmation dialog completes.
        // Enter the confirmation phase synchronously so only one prompt/mutation can exist.
        if (!UserAccessChangePolicy.CanStartConfirmation(isConfirming, isSubmitting)
            || !CanSubmit)
            return;

        if (SelectedRole is not { } selectedRole)
        {
            errorMessage = UiStrings.Users.RoleRequired;
            return;
        }

        if (!HasPendingChange)
            return;

        var confirmation = string.Format(
            UiStrings.Users.ConfirmAccessChange,
            DisplayName,
            TargetUserTypeLabel,
            selectedRole.Name);

        isConfirming = true;
        bool confirmed;
        try
        {
            confirmed = await DialogService.Confirm(
                confirmation,
                UiStrings.Users.ChangeAccessTitle,
                new ConfirmOptions
                {
                    OkButtonText = UiStrings.Users.ChangeAccess,
                    CancelButtonText = UiStrings.Common.Cancel
                }) ?? false;
        }
        finally
        {
            if (!disposeCts.IsCancellationRequested)
                isConfirming = false;
        }

        if (!confirmed || disposeCts.IsCancellationRequested)
            return;

        isSubmitting = true;
        try
        {
            await Identity.ChangeUserAccessAsync(
                UserId,
                new AssignRoleRequest(selectedRole.Id),
                RowVersion,
                disposeCts.Token);
            DialogService.Close(true);
        }
        catch (OperationCanceledException) when (disposeCts.IsCancellationRequested)
        {
            // Closing the dialog cancels the in-flight mutation.
        }
        catch (ApiException ex)
        {
            var recovery = UserAccessChangePolicy.RecoveryForProblemCode(ex.ProblemCode);
            if (recovery == UserAccessRecoveryAction.CloseAndReloadUser)
            {
                isStale = true;
                errorMessage = UiStrings.Users.AccessChangedConcurrently;
            }
            else
            {
                errorMessage = AccessChangeError(ex);
                if (recovery == UserAccessRecoveryAction.ReloadRoles)
                {
                    model.RoleId = null;
                    await LoadRolesAsync(selectCurrentRole: false);
                }
            }
        }
        finally
        {
            if (!disposeCts.IsCancellationRequested)
                isSubmitting = false;
        }
    }

    internal static string AccessChangeError(ApiException exception) =>
        exception.ProblemCode switch
        {
            "Identity.User.CannotAssignRoleSelf" => UiStrings.Users.CannotChangeOwnAccess,
            "Identity.User.AssignRoleForbidden" => UiStrings.Users.AccessChangeForbidden,
            "Identity.User.ChangeAccountTypeForbidden" => UiStrings.Users.ChangeAccountTypeForbidden,
            "Identity.User.AccountTypeTransitionNotAllowed" => UiStrings.Users.AccountTypeTransitionNotAllowed,
            "Identity.User.IncompatibleRole" => UiStrings.Users.AccountTypeTransitionNotAllowed,
            "Identity.User.LastAdmin" => UiStrings.Users.LastAdministratorRequired,
            "Identity.User.PermissionDelegationForbidden" => UiStrings.Users.PermissionDelegationForbidden,
            "Identity.User.ManagedRoleForbidden" => UiStrings.Users.ManagedRoleForbidden,
            "Identity.User.RoleNotFound" => UiStrings.Users.SelectedRoleUnavailable,
            "Identity.User.CurrentRoleNotFound" => UiStrings.Users.AccessChangedConcurrently,
            "Identity.User.CurrentRoleIncompatible" => UiStrings.Users.AccessChangedConcurrently,
            "Identity.User.CurrentRoleInvalid" => UiStrings.Users.AccessChangedConcurrently,
            "Identity.User.SelectedRoleInvalid" => UiStrings.Users.SelectedRoleUnavailable,
            "Identity.Role.UnknownPermission" => UiStrings.Users.SelectedRoleUnavailable,
            "Identity.Role.IncompatiblePermission" => UiStrings.Users.SelectedRoleUnavailable,
            "Identity.Role.ViewerPagePermissionRequired" => UiStrings.Users.SelectedRoleUnavailable,
            "Identity.User.Deactivated" => UiStrings.Users.DeactivatedAccessImmutable,
            "Identity.User.LoginEmailReleased" => UiStrings.Users.DeactivatedAccessImmutable,
            "Identity.User.ExternalReferenceNotAllowed" => UiStrings.Users.AccountTypeTransitionNotAllowed,
            "Identity.User.ExternalReferenceRequired" => UiStrings.Users.AccountTypeTransitionNotAllowed,
            "General.ConcurrencyConflict" => UiStrings.Users.AccessChangedConcurrently,
            "General.PreconditionRequired" => UiStrings.Users.AccessChangedConcurrently,
            _ => exception.ToDisplayMessage(UiStrings.Common.SomethingWentWrong)
        };

    private static string UserTypeLabel(string userType) => userType switch
    {
        UserTypes.SystemAdministrator => UiStrings.Users.TypeSystemAdministrator,
        UserTypes.ViewerOnly => UiStrings.Users.TypeViewerOnly,
        UserTypes.StationStaff => UiStrings.Users.TypeStationStaff,
        UserTypes.CustomerContact => UiStrings.Users.TypeCustomerContact,
        _ => userType
    };

    public ValueTask DisposeAsync()
    {
        roleLoadGeneration++;
        disposeCts.Cancel();
        roleLoadCts?.Cancel();
        roleLoadCts?.Dispose();
        disposeCts.Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed class AccessChangeModel
    {
        public Guid? RoleId { get; set; }
    }

    private sealed record UserTypeOption(string Value, string Label);
}
