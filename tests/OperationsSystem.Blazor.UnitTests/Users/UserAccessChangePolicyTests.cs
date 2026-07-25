using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Features.Users;
using OperationsSystem.Blazor.Client.Features.Users.Components;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.Users;

public sealed class UserAccessChangePolicyTests
{
    [Theory]
    [InlineData(UserTypes.SystemAdministrator)]
    [InlineData(UserTypes.ViewerOnly)]
    public void Direct_user_with_permission_can_target_both_direct_types(string currentType)
    {
        UserAccessChangePolicy.AllowedTargetTypes(currentType, canChangeAccountType: true)
            .ShouldBe(UserTypes.Direct);
    }

    [Theory]
    [InlineData(UserTypes.SystemAdministrator)]
    [InlineData(UserTypes.ViewerOnly)]
    [InlineData(UserTypes.StationStaff)]
    [InlineData(UserTypes.CustomerContact)]
    public void Without_change_type_permission_only_current_type_is_available(string currentType)
    {
        UserAccessChangePolicy.AllowedTargetTypes(currentType, canChangeAccountType: false)
            .ShouldBe([currentType]);
    }

    [Theory]
    [InlineData(UserTypes.StationStaff)]
    [InlineData(UserTypes.CustomerContact)]
    public void Linked_type_remains_immutable_even_with_permission(string currentType)
    {
        UserAccessChangePolicy.AllowedTargetTypes(currentType, canChangeAccountType: true)
            .ShouldBe([currentType]);
    }

    [Fact]
    public void Change_action_is_hidden_for_self()
    {
        var userId = Guid.NewGuid();

        UserAccessChangePolicy.CanShowAction(userId, userId, "Active").ShouldBeFalse();
    }

    [Fact]
    public void Change_action_is_hidden_for_deactivated_user()
    {
        UserAccessChangePolicy.CanShowAction(Guid.NewGuid(), Guid.NewGuid(), "Deactivated")
            .ShouldBeFalse();
    }

    [Fact]
    public void Change_action_is_visible_for_another_live_user()
    {
        UserAccessChangePolicy.CanShowAction(Guid.NewGuid(), Guid.NewGuid(), "Active")
            .ShouldBeTrue();
    }

    [Fact]
    public void Same_type_and_role_is_a_no_op()
    {
        var roleId = Guid.NewGuid();

        UserAccessChangePolicy.IsNoOp(
            UserTypes.ViewerOnly,
            roleId,
            UserTypes.ViewerOnly,
            roleId).ShouldBeTrue();
    }

    [Fact]
    public void Type_change_is_not_a_no_op_even_if_role_id_matches()
    {
        var roleId = Guid.NewGuid();

        UserAccessChangePolicy.IsNoOp(
            UserTypes.SystemAdministrator,
            roleId,
            UserTypes.ViewerOnly,
            roleId).ShouldBeFalse();
    }

    [Fact]
    public void Supported_change_requires_confirmation()
    {
        UserAccessChangePolicy.RequiresConfirmation(
            UserTypes.ViewerOnly,
            Guid.NewGuid(),
            UserTypes.SystemAdministrator,
            Guid.NewGuid(),
            canChangeAccountType: true).ShouldBeTrue();
    }

    [Fact]
    public void Cross_type_change_without_permission_cannot_reach_confirmation()
    {
        UserAccessChangePolicy.RequiresConfirmation(
            UserTypes.ViewerOnly,
            Guid.NewGuid(),
            UserTypes.SystemAdministrator,
            Guid.NewGuid(),
            canChangeAccountType: false).ShouldBeFalse();
    }

    [Fact]
    public void Latest_non_canceled_role_load_is_accepted()
    {
        UserAccessChangePolicy.ShouldAcceptRoleLoadResult(
            requestGeneration: 3,
            currentGeneration: 3,
            isCanceled: false).ShouldBeTrue();
    }

    [Theory]
    [InlineData(2, 3, false)]
    [InlineData(3, 3, true)]
    public void Superseded_or_canceled_role_load_is_ignored(
        int requestGeneration,
        int currentGeneration,
        bool isCanceled)
    {
        UserAccessChangePolicy.ShouldAcceptRoleLoadResult(
            requestGeneration,
            currentGeneration,
            isCanceled).ShouldBeFalse();
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void Confirmation_is_single_flight(
        bool isConfirming,
        bool isSubmitting,
        bool expected)
    {
        UserAccessChangePolicy.CanStartConfirmation(isConfirming, isSubmitting)
            .ShouldBe(expected);
    }

    [Theory]
    [InlineData("General.ConcurrencyConflict")]
    [InlineData("General.PreconditionRequired")]
    public void Stale_access_errors_use_the_concurrency_message(string problemCode)
    {
        var exception = new ApiException(
            409,
            $$"""{"code":"{{problemCode}}","detail":"server detail"}""");

        var message = ChangeUserAccessDialog.AccessChangeError(exception);

        new[]
        {
            "Another administrator changed this user. Close the dialog, reload the user, and review the latest access.",
            "قام مسؤول آخر بتغيير هذا المستخدم. أغلق النافذة وأعد تحميل المستخدم ثم راجع أحدث إعدادات الوصول."
        }.ShouldContain(message);
        UserAccessChangePolicy.IsStaleProblemCode(problemCode).ShouldBeTrue();
    }

    [Fact]
    public void Managed_role_ceiling_failure_uses_the_specific_localized_message()
    {
        var exception = new ApiException(
            403,
            """{"code":"Identity.User.ManagedRoleForbidden","detail":"server detail"}""");

        var message = ChangeUserAccessDialog.AccessChangeError(exception);

        new[]
        {
            "You cannot change this user because their current role grants permissions you do not hold.",
            "لا يمكنك تغيير هذا المستخدم لأن دوره الحالي يمنح صلاحيات لا تملكها."
        }.ShouldContain(message);
    }

    [Theory]
    [InlineData("Identity.User.RoleNotFound")]
    [InlineData("Identity.User.SelectedRoleInvalid")]
    [InlineData("Identity.Role.UnknownPermission")]
    [InlineData("Identity.Role.IncompatiblePermission")]
    [InlineData("Identity.Role.ViewerPagePermissionRequired")]
    public void Role_drift_failures_ask_the_administrator_to_reload_roles(string problemCode)
    {
        var exception = new ApiException(
            409,
            $$"""{"code":"{{problemCode}}","detail":"server detail"}""");

        var message = ChangeUserAccessDialog.AccessChangeError(exception);

        new[]
        {
            "The selected role is no longer available. The role list was refreshed; choose again.",
            "لم يعد الدور المحدد متاحًا. تم تحديث قائمة الأدوار؛ اختر مرة أخرى."
        }.ShouldContain(message);
        UserAccessChangePolicy.RecoveryForProblemCode(problemCode)
            .ShouldBe(UserAccessRecoveryAction.ReloadRoles);
    }

    [Theory]
    [InlineData("Identity.User.CurrentRoleNotFound")]
    [InlineData("Identity.User.CurrentRoleIncompatible")]
    [InlineData("Identity.User.CurrentRoleInvalid")]
    public void Current_role_drift_requires_closing_and_reloading_the_user(string problemCode)
    {
        var exception = new ApiException(
            409,
            $$"""{"code":"{{problemCode}}","detail":"server detail"}""");

        var message = ChangeUserAccessDialog.AccessChangeError(exception);

        new[]
        {
            "Another administrator changed this user. Close the dialog, reload the user, and review the latest access.",
            "قام مسؤول آخر بتغيير هذا المستخدم. أغلق النافذة وأعد تحميل المستخدم ثم راجع أحدث إعدادات الوصول."
        }.ShouldContain(message);
        UserAccessChangePolicy.RecoveryForProblemCode(problemCode)
            .ShouldBe(UserAccessRecoveryAction.CloseAndReloadUser);
    }

    [Theory]
    [InlineData(UserTypes.ViewerOnly, UserTypes.SystemAdministrator, UserAccessChangeKind.ElevationToAdministrator)]
    [InlineData(UserTypes.SystemAdministrator, UserTypes.ViewerOnly, UserAccessChangeKind.DemotionToViewer)]
    [InlineData(UserTypes.ViewerOnly, UserTypes.ViewerOnly, UserAccessChangeKind.RoleOnly)]
    [InlineData(UserTypes.StationStaff, UserTypes.CustomerContact, UserAccessChangeKind.Unsupported)]
    public void Classifies_access_change(
        string currentType,
        string targetType,
        UserAccessChangeKind expected)
    {
        UserAccessChangePolicy.Classify(currentType, targetType).ShouldBe(expected);
    }
}
