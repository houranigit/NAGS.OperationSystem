using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Auth;
using Shouldly;

namespace OperationsSystem.Blazor.UnitTests.Auth;

public sealed class PortalNavigationPolicyTests
{
    [Fact]
    public void Viewer_only_is_available_as_a_direct_account_type()
    {
        UserTypes.Direct.ShouldBe([UserTypes.SystemAdministrator, UserTypes.ViewerOnly]);
        UserTypes.All.ShouldContain(UserTypes.ViewerOnly);
    }

    [Fact]
    public void Dashboard_only_viewer_lands_on_dashboard_without_sidebar()
    {
        var permissions = new[] { OperationsPermissions.DashboardView };

        PortalNavigationPolicy.ResolvePostLogin(UserTypes.ViewerOnly, permissions, null)
            .ShouldBe("/");
        PortalNavigationPolicy.ShouldShowSidebar(UserTypes.ViewerOnly, permissions)
            .ShouldBeFalse();
    }

    [Fact]
    public void Operations_dashboard_only_viewer_lands_there_without_sidebar()
    {
        var permissions = new[] { OperationsPermissions.DashboardAnalyticsView };

        PortalNavigationPolicy.ResolvePostLogin(UserTypes.ViewerOnly, permissions, null)
            .ShouldBe("/operations/dashboard");
        PortalNavigationPolicy.ShouldShowSidebar(UserTypes.ViewerOnly, permissions)
            .ShouldBeFalse();
    }

    [Fact]
    public void Both_dashboards_land_on_main_dashboard_and_show_sidebar()
    {
        var permissions = new[]
        {
            OperationsPermissions.DashboardAnalyticsView,
            OperationsPermissions.DashboardView
        };

        PortalNavigationPolicy.ResolvePostLogin(UserTypes.ViewerOnly, permissions, null)
            .ShouldBe("/");
        PortalNavigationPolicy.ShouldShowSidebar(UserTypes.ViewerOnly, permissions)
            .ShouldBeTrue();
    }

    [Fact]
    public void Flights_view_grants_calendar_and_list_as_two_destinations()
    {
        var destinations = PortalNavigationPolicy
            .GetPrimaryDestinations([OperationsPermissions.FlightsView]);

        destinations.Select(destination => destination.Path)
            .ShouldBe(["/operations/calendar", "/operations/flights"]);
        PortalNavigationPolicy.ShouldShowSidebar(
                UserTypes.ViewerOnly,
                [OperationsPermissions.FlightsView])
            .ShouldBeTrue();
    }

    [Fact]
    public void Landing_order_is_deterministic_and_follows_the_navigation_catalog()
    {
        var permissions = new[]
        {
            MasterDataPermissions.CustomersView,
            IdentityPermissions.UsersView,
            OperationsPermissions.WorkOrdersView
        };

        PortalNavigationPolicy.GetPrimaryDestinations(permissions)
            .Select(destination => destination.Path)
            .ShouldBe(["/users", "/operations/work-orders", "/master-data/customers"]);
        PortalNavigationPolicy.ResolveLandingPage(permissions).ShouldBe("/users");
    }

    [Fact]
    public void Authorized_detail_return_url_is_preserved()
    {
        var flightId = Guid.NewGuid();
        var returnUrl = $"/operations/flights/{flightId}?tab=work-orders#latest";

        PortalNavigationPolicy.ResolvePostLogin(
                UserTypes.ViewerOnly,
                [OperationsPermissions.FlightsView],
                returnUrl)
            .ShouldBe(returnUrl);
    }

    [Theory]
    [InlineData("/users")]
    [InlineData("/users/not-a-guid")]
    [InlineData("/operations/dashboard")]
    [InlineData("/unknown")]
    public void Unauthorized_or_unknown_viewer_return_url_falls_back_to_landing(string returnUrl)
    {
        PortalNavigationPolicy.ResolvePostLogin(
                UserTypes.ViewerOnly,
                [OperationsPermissions.DashboardView],
                returnUrl)
            .ShouldBe("/");
    }

    [Theory]
    [InlineData("//example.com")]
    [InlineData("https://example.com")]
    [InlineData("/%2f%2fexample.com")]
    [InlineData("/users\\..\\account")]
    [InlineData("/login")]
    public void Unsafe_or_login_return_url_is_not_honored(string returnUrl)
    {
        PortalNavigationPolicy.ResolvePostLogin(
                UserTypes.ViewerOnly,
                [OperationsPermissions.DashboardAnalyticsView],
                returnUrl)
            .ShouldBe("/operations/dashboard");
    }

    [Fact]
    public void Utility_return_urls_are_allowed_but_do_not_count_as_destinations()
    {
        PortalNavigationPolicy.ResolvePostLogin(UserTypes.ViewerOnly, [], "/account?tab=security")
            .ShouldBe("/account?tab=security");
        PortalNavigationPolicy.GetPrimaryDestinations([]).ShouldBeEmpty();
        PortalNavigationPolicy.ResolveLandingPage([]).ShouldBe(PortalNavigationPolicy.FallbackPath);
        PortalNavigationPolicy.ShouldShowSidebar(UserTypes.ViewerOnly, []).ShouldBeFalse();
    }

    [Fact]
    public void Existing_account_types_keep_their_previous_landing_and_sidebar_behavior()
    {
        PortalNavigationPolicy.ResolvePostLogin(
                UserTypes.StationStaff,
                [],
                "/operations/flights?status=active")
            .ShouldBe("/operations/flights?status=active");
        PortalNavigationPolicy.ResolvePostLogin(UserTypes.CustomerContact, [], null)
            .ShouldBe("/");
        PortalNavigationPolicy.ShouldShowSidebar(UserTypes.StationStaff, [])
            .ShouldBeTrue();
    }
}
