using OperationsSystem.Blazor.Client.Auth;
using OperationsSystem.Blazor.Client.Pages;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.Pages;

public sealed class DashboardFeatureCatalogTests
{
    [Fact]
    public void Feature_cards_include_only_authorized_destinations_plus_account()
    {
        var features = DashboardFeatureCatalog.BuildFeatures(
        [
            OperationsPermissions.FlightsView,
            MasterDataPermissions.ServicesView,
            MasterDataPermissions.ToolsView
        ]);

        features.Select(feature => feature.Key)
            .ShouldBe(["flights", "master-data", "account"]);
        features.Single(feature => feature.Key == "master-data").Href
            .ShouldBe("/master-data/services");
    }

    [Fact]
    public void Administration_card_uses_the_first_authorized_administration_route()
    {
        var features = DashboardFeatureCatalog.BuildFeatures(
        [
            IdentityPermissions.RolesView,
            AuditPermissions.TrailsView
        ]);

        var administration = features.Single(feature => feature.Key == "administration");
        administration.Href.ShouldBe("/roles");
        administration.Description.ShouldContain("roles");
        administration.Description.ShouldContain("audit trail");
    }

    [Fact]
    public void Mutating_quick_actions_require_their_view_permission_too()
    {
        DashboardFeatureCatalog.BuildQuickActions(
            [OperationsPermissions.FlightsSchedule, OperationsPermissions.WorkOrdersAuthor])
            .Select(action => action.Key)
            .ShouldBe(["account"]);

        DashboardFeatureCatalog.BuildQuickActions(
            [
                OperationsPermissions.FlightsView,
                OperationsPermissions.FlightsSchedule,
                OperationsPermissions.WorkOrdersView,
                OperationsPermissions.WorkOrdersAuthor
            ])
            .Select(action => action.Key)
            .ShouldBe(["schedule-flight", "create-work-order"]);
    }
}
