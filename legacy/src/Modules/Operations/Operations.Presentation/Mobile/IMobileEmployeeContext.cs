using Core.Contracts.Features.Employee;

namespace Operations.Presentation.Mobile;

/// <summary>
/// Resolves the calling user's <see cref="EmployeeSnapshot"/> from the JWT
/// <c>sub</c> claim once per request.  Every mobile endpoint uses this to scope
/// queries / commands by employee identity instead of trusting client-supplied ids.
/// Cached in <c>HttpContext.Items</c> so a single request that hits multiple
/// handlers only pays the lookup cost once.
/// </summary>
public interface IMobileEmployeeContext
{
    /// <summary>
    /// Returns the resolved employee for the current request, or <c>null</c> if the
    /// JWT <c>sub</c> isn't linked to an active employee.  Caller is expected to
    /// short-circuit to 401/403 in that case.
    /// </summary>
    Task<EmployeeSnapshot?> GetCurrentEmployeeAsync(CancellationToken cancellationToken = default);
}
