using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BuildingBlocks.Contracts.Authorization;
using Identity.Application.Abstractions;
using Identity.Domain.Roles;
using Identity.Domain.Users;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Identity.IntegrationTests;

public sealed class WorkOrderEmailPreferenceApiTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>
{
    private const string Base = IdentityApiTestData.Base;

    [Fact]
    public async Task Anonymous_user_cannot_read_or_change_the_preference()
    {
        using var client = factory.CreateClient();

        (await client.GetAsync($"{Base}/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PutAsJsonAsync($"{Base}/me/work-order-email-preference", new { enabled = true }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Employee_without_management_permissions_can_only_change_own_preference_and_mobile_sees_it()
    {
        var employee = await AddEmployeeAsync();
        var otherEmployee = await AddEmployeeAsync();
        using var portal = await LoginAsync(employee.Email, mobile: false);
        var original = await portal.GetFromJsonAsync<MeResponse>($"{Base}/me");
        original!.ReceiveWorkOrderSubmissionEmails.ShouldBeFalse();
        original.Permissions.ShouldBeEmpty();

        // A supplied user id cannot redirect this self-service operation to another account.
        var saved = await portal.PutAsJsonAsync($"{Base}/me/work-order-email-preference",
            new { enabled = true, userId = otherEmployee.Id });
        saved.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var mobile = await LoginAsync(employee.Email, mobile: true);
        (await mobile.GetFromJsonAsync<MeResponse>($"{Base}/me"))!.ReceiveWorkOrderSubmissionEmails.ShouldBeTrue();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            (await db.Users.AsNoTracking().SingleAsync(u => u.Id == otherEmployee.Id))
                .ReceiveWorkOrderSubmissionEmails.ShouldBeFalse();
        }

        (await mobile.PutAsJsonAsync($"{Base}/me/work-order-email-preference", new { enabled = false }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await portal.GetFromJsonAsync<MeResponse>($"{Base}/me"))!.ReceiveWorkOrderSubmissionEmails.ShouldBeFalse();
    }

    private async Task<(Guid Id, string Email)> AddEmployeeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = DateTimeOffset.UtcNow;
        var role = Role.Create($"Email preference staff {Guid.NewGuid():N}", null, [], UserType.StationStaff, now).Value;
        var user = User.Invite(Email.Create($"staff-{Guid.NewGuid():N}@example.com").Value,
            "Employee", role.Id, "test-token", now.AddHours(1), now, UserType.StationStaff, Guid.NewGuid()).Value;
        user.Activate("test-token", hasher.Hash(IdentityApiTestData.DemoUserPassword), now).IsSuccess.ShouldBeTrue();
        db.Roles.Add(role);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (user.Id, user.Email.Value);
    }

    private async Task<HttpClient> LoginAsync(string email, bool mobile)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync($"{Base}/auth/{(mobile ? "mobile/" : string.Empty)}login",
            new { email, password = IdentityApiTestData.DemoUserPassword });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
        return client;
    }

    private sealed record TokenResponse(string AccessToken);
    private sealed record MeResponse(bool ReceiveWorkOrderSubmissionEmails, IReadOnlyList<string> Permissions);
}
