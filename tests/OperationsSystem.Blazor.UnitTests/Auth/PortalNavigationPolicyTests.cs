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
    public void Flights_viewer_lands_on_flights_and_can_navigate_to_work_orders_and_calendar()
    {
        var permissions = new[] { OperationsPermissions.FlightsView };

        PortalNavigationPolicy.ResolvePostLogin(UserTypes.ViewerOnly, permissions, null)
            .ShouldBe("/operations/flights");
        PortalNavigationPolicy.GetPrimaryDestinations(permissions)
            .Select(destination => destination.Path)
            .ShouldBe(["/operations/flights", "/operations/work-orders", "/operations/calendar"]);
        PortalNavigationPolicy.ShouldShowSidebar(UserTypes.ViewerOnly, permissions)
            .ShouldBeTrue();
    }

    [Theory]
    [InlineData("/operations/work-orders?search=123")]
    [InlineData("/operations/calendar")]
    public void Operations_direct_link_requires_the_flights_view_permission(string returnUrl)
    {
        PortalNavigationPolicy.ResolvePostLogin(
                UserTypes.ViewerOnly,
                [OperationsPermissions.FlightsView],
                returnUrl)
            .ShouldBe(returnUrl);
        PortalNavigationPolicy.IsRouteAuthorized(returnUrl, [MasterDataPermissions.StaffAllocationView])
            .ShouldBeFalse();
    }

    [Fact]
    public void Staff_allocation_viewer_can_land_on_the_grid_and_open_a_direct_link()
    {
        const string route = "/operations/staff-allocation";
        var permissions = new[] { MasterDataPermissions.StaffAllocationView };

        PortalNavigationPolicy.ResolvePostLogin(UserTypes.ViewerOnly, permissions, null)
            .ShouldBe(route);
        PortalNavigationPolicy.IsRouteAuthorized(route, permissions)
            .ShouldBeTrue();
    }

    [Fact]
    public void Dashboard_viewer_can_open_the_dashboard_with_analytics_permission()
    {
        const string route = "/operations/dashboard";
        var permissions = new[] { OperationsPermissions.DashboardAnalyticsView };

        PortalNavigationPolicy.ResolvePostLogin(UserTypes.ViewerOnly, permissions, null).ShouldBe(route);
        PortalNavigationPolicy.IsRouteAuthorized(route, permissions).ShouldBeTrue();
        PortalNavigationPolicy.IsRouteAuthorized(route, [OperationsPermissions.DashboardExport]).ShouldBeFalse();
    }

    [Fact]
    public void Landing_order_is_deterministic_and_follows_the_navigation_catalog()
    {
        var permissions = new[]
        {
            MasterDataPermissions.CustomersView,
            IdentityPermissions.UsersView,
            OperationsPermissions.FlightsView
        };

        PortalNavigationPolicy.GetPrimaryDestinations(permissions)
            .Select(destination => destination.Path)
            .ShouldBe(["/users", "/operations/flights", "/operations/work-orders", "/operations/calendar", "/master-data/customers"]);
        PortalNavigationPolicy.ResolveLandingPage(permissions).ShouldBe("/users");
    }

    [Fact]
    public void Authorized_grid_return_url_is_preserved()
    {
        const string returnUrl = "/operations/flights?search=123";

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
    [InlineData("/operations/staff-allocation")]
    [InlineData("/operations/flights/00000000-0000-0000-0000-000000000001")]
    [InlineData("/unknown")]
    public void Unauthorized_or_unknown_viewer_return_url_falls_back_to_landing(string returnUrl)
    {
        PortalNavigationPolicy.ResolvePostLogin(
                UserTypes.ViewerOnly,
                [OperationsPermissions.FlightsView],
                returnUrl)
            .ShouldBe("/operations/flights");
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
                [OperationsPermissions.FlightsView],
                returnUrl)
            .ShouldBe("/operations/flights");
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
