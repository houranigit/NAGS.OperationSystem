using System.Security.Claims;
using Core.Contracts.Features.Employee;
using Core.Contracts.Readers;
using Microsoft.AspNetCore.Http;

namespace Operations.Presentation.Mobile;

/// <summary>
/// Default <see cref="IMobileEmployeeContext"/> backed by <see cref="IHttpContextAccessor"/>:
/// reads the JWT <c>sub</c>, asks <see cref="IEmployeeReader.GetByLinkedUserIdAsync"/>
/// for the linked employee, and caches the resolved snapshot per-request inside
/// <c>HttpContext.Items</c>.
/// </summary>
public sealed class HttpContextMobileEmployeeContext(
    IHttpContextAccessor httpContextAccessor,
    IEmployeeReader employeeReader)
    : IMobileEmployeeContext
{
    private const string CacheKey = "__mobile_employee_snapshot";

    /// <summary>
    /// JWT subject claim. Hard-coded so we don't drag the
    /// <c>System.IdentityModel.Tokens.Jwt</c> package into the presentation project — the
    /// Identity issuer always sets <c>sub</c> to the string form of the user's <see cref="Guid"/> id.
    /// </summary>
    private const string SubjectClaimType = "sub";

    public async Task<EmployeeSnapshot?> GetCurrentEmployeeAsync(CancellationToken cancellationToken = default)
    {
        var http = httpContextAccessor.HttpContext;
        if (http is null)
            return null;

        if (http.Items.TryGetValue(CacheKey, out var cached) && cached is EmployeeSnapshot snapshot)
            return snapshot;

        var sub = http.User.FindFirstValue(SubjectClaimType)
                  ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(sub, out var userId) || userId == Guid.Empty)
            return null;

        var employee = await employeeReader.GetByLinkedUserIdAsync(userId, cancellationToken);
        if (employee is not null)
            http.Items[CacheKey] = employee;

        return employee;
    }
}
