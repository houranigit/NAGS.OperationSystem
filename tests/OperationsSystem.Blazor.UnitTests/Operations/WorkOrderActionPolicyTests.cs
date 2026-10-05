using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Auth;
using OperationsSystem.Blazor.Client.Features.Operations;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.Operations;

public sealed class WorkOrderActionPolicyTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid OtherUser = Guid.NewGuid();
    private static readonly string[] AllPermissions =
    [
        OperationsPermissions.WorkOrdersView,
        OperationsPermissions.WorkOrdersAuthor,
        OperationsPermissions.WorkOrdersApprove,
        OperationsPermissions.WorkOrdersMerge,
        OperationsPermissions.WorkOrdersManageOthers,
        OperationsPermissions.WorkOrdersDeleteOthers
    ];

    [Theory]
    [InlineData("Submitted")]
    [InlineData("Returned")]
    public void Owners_with_action_permissions_can_edit_approve_merge_and_delete_mutable_orders(string status)
    {
        var available = WorkOrderActionPolicy.Available(Order(status), Owner, AllPermissions);

        available.ShouldBe(new(true, true, true, false, true, true));
    }

    [Theory]
    [InlineData("Approved", true)]
    [InlineData("Merged", false)]
    [InlineData("Deleted", false)]
    public void Final_orders_cannot_be_edited_approved_merged_or_deleted(string status, bool canReturn)
    {
        WorkOrderActionPolicy.Available(Order(status), Owner, AllPermissions)
            .ShouldBe(new(true, false, false, canReturn, false, false));
    }

    [Fact]
    public void Read_only_users_only_see_view()
    {
        WorkOrderActionPolicy.Available(Order(), Owner, [OperationsPermissions.WorkOrdersView])
            .ShouldBe(new(true, false, false, false, false, false));
    }

    [Fact]
    public void Work_order_view_permission_is_required_for_every_submenu_action()
    {
        var withoutView = AllPermissions.Where(permission => permission != OperationsPermissions.WorkOrdersView).ToArray();

        WorkOrderActionPolicy.Available(Order(), Owner, withoutView)
            .ShouldBe(new(false, false, false, false, false, false));
    }

    [Fact]
    public void Authors_cannot_edit_or_delete_other_peoples_orders_without_the_separate_permissions()
    {
        WorkOrderActionPolicy.Available(Order(), OtherUser,
                [OperationsPermissions.WorkOrdersView, OperationsPermissions.WorkOrdersAuthor])
            .ShouldBe(new(true, false, false, false, false, false));
    }

    [Fact]
    public void Manage_others_does_not_grant_delete_others()
    {
        var available = WorkOrderActionPolicy.Available(Order(), OtherUser,
            [OperationsPermissions.WorkOrdersView, OperationsPermissions.WorkOrdersAuthor, OperationsPermissions.WorkOrdersManageOthers]);

        available.Edit.ShouldBeTrue();
        available.Delete.ShouldBeFalse();
    }

    [Fact]
    public void Delete_others_does_not_grant_manage_others()
    {
        var available = WorkOrderActionPolicy.Available(Order(), OtherUser,
            [OperationsPermissions.WorkOrdersView, OperationsPermissions.WorkOrdersAuthor, OperationsPermissions.WorkOrdersDeleteOthers]);

        available.Edit.ShouldBeFalse();
        available.Delete.ShouldBeTrue();
    }

    [Fact]
    public void Manage_and_delete_others_permissions_do_not_replace_author_permission()
    {
        var available = WorkOrderActionPolicy.Available(Order(), OtherUser,
            [OperationsPermissions.WorkOrdersView, OperationsPermissions.WorkOrdersManageOthers, OperationsPermissions.WorkOrdersDeleteOthers]);

        available.Edit.ShouldBeFalse();
        available.Delete.ShouldBeFalse();
    }

    [Fact]
    public void Approvers_can_approve_others_orders_without_receiving_author_actions()
    {
        WorkOrderActionPolicy.Available(Order(), OtherUser,
                [OperationsPermissions.WorkOrdersView, OperationsPermissions.WorkOrdersApprove])
            .ShouldBe(new(true, false, true, false, false, false));
    }

    private static WorkOrderSummaryModel Order(string status = "Submitted") =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Completion", status, null, Owner, "Owner", "row-version");
}
