using Microsoft.AspNetCore.Components;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Auth;
using OperationsSystem.Blazor.Client.Shared;
using OperationsSystem.Blazor.Client.State;
using Radzen;

namespace OperationsSystem.Blazor.Client.Features.Operations.Pages;

public partial class StaffAllocationPage
{
    [Inject] private MasterDataApiClient MasterData { get; set; } = default!;
    [Inject] private AuthSession Auth { get; set; } = default!;
    [Inject] private GridPreferences GridPrefs { get; set; } = default!;

    private const string GridKey = "staff-allocation";
    private static readonly int[] PageSizes = [10, 25, 50, 100];

    private DataListCard<StaffAllocationMember>? list;
    private IReadOnlyList<StaffAllocationStation> stations = [];
    private IReadOnlyDictionary<Guid, StaffAllocationStation> stationsById = new Dictionary<Guid, StaffAllocationStation>();
    private IReadOnlyList<StaffAllocationMember> staff = [];
    private IReadOnlyList<StaffAllocationMember> items = [];
    private IReadOnlyList<FilterOption> stationOptions = [];
    private IReadOnlyList<FilterOption> manpowerOptions = [];
    private IReadOnlyList<FilterOption> licenseOptions = [];

    private int totalCount;
    private int currentSkip;
    private int currentPageSize = GridPreferences.DefaultPageSize;
    private bool pageSizeReady;
    private bool isLoading;
    private bool loadError;
    private bool isMoving;
    private string searchTerm = string.Empty;
    private Guid? stationFilter;
    private Guid? manpowerFilter;
    private Guid? licenseFilter;
    private StationMove? pendingMove;
    private string? moveError;
    private string? moveSuccess;

    private bool CanReassignStaff => Auth.IsSystemAdministrator && Auth.HasPermission(MasterDataPermissions.StaffAllocationReassign);
    private int ActiveFilterCount => (stationFilter.HasValue ? 1 : 0) + (manpowerFilter.HasValue ? 1 : 0) + (licenseFilter.HasValue ? 1 : 0);

    protected override async Task OnInitializedAsync()
    {
        if (!Auth.HasGlobalReadScope || !Auth.HasPermission(MasterDataPermissions.StaffAllocationView))
            return;

        currentPageSize = await GridPrefs.GetPageSizeAsync(GridKey, currentPageSize, PageSizes);
        pageSizeReady = true;
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (isLoading)
            return;

        isLoading = true;

        try
        {
            var overview = await MasterData.GetStaffAllocationAsync();
            stations = overview.Stations.OrderBy(station => station.IataCode).ToArray();
            stationsById = stations.ToDictionary(station => station.Id);
            staff = overview.StaffMembers.OrderBy(member => member.FullName).ThenBy(member => member.Id).ToArray();

            stationOptions = stations.Select(station => new FilterOption(StationLabel(station), station.Id)).ToArray();
            manpowerOptions = staff
                .DistinctBy(member => member.ManpowerTypeId)
                .Select(member => new FilterOption(member.ManpowerTypeName, member.ManpowerTypeId))
                .OrderBy(option => option.Label)
                .ToArray();
            licenseOptions = staff.SelectMany(member => member.Licenses)
                .DistinctBy(license => license.LicenseId)
                .Select(license => new FilterOption($"{license.Code} · {license.Name}", license.LicenseId))
                .OrderBy(option => option.Label)
                .ToArray();

            loadError = false;
            ApplyGridPage();
            if (currentSkip > 0 && currentSkip >= totalCount)
                await ReloadGridAsync();
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException)
        {
            loadError = true;
        }
        finally
        {
            isLoading = false;
        }
    }

    private async Task LoadDataAsync(LoadDataArgs args)
    {
        await CancelMoveAsync();
        currentSkip = args.Skip ?? 0;
        currentPageSize = args.Top ?? currentPageSize;
        ApplyGridPage();
    }

    private void ApplyGridPage()
    {
        var term = searchTerm.Trim();
        var matches = staff.Where(member => MatchesFilters(member, term)).ToArray();
        totalCount = matches.Length;
        items = matches.Skip(currentSkip).Take(currentPageSize).ToArray();
    }

    private bool MatchesFilters(StaffAllocationMember member, string term) =>
        (!stationFilter.HasValue || member.StationId == stationFilter)
        && (!manpowerFilter.HasValue || member.ManpowerTypeId == manpowerFilter)
        && (!licenseFilter.HasValue || member.Licenses.Any(license => license.LicenseId == licenseFilter))
        && (term.Length == 0
            || member.FullName.Contains(term, StringComparison.OrdinalIgnoreCase)
            || member.EmployeeId.Contains(term, StringComparison.OrdinalIgnoreCase)
            || member.ManpowerTypeName.Contains(term, StringComparison.OrdinalIgnoreCase)
            || member.Licenses.Any(license => license.Code.Contains(term, StringComparison.OrdinalIgnoreCase)
                || license.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || license.LicenseNumber.Contains(term, StringComparison.OrdinalIgnoreCase)));

    private async Task ReloadGridAsync()
    {
        await CancelMoveAsync();
        if (list is not null)
            await list.ReloadAsync();
        else
        {
            currentSkip = 0;
            ApplyGridPage();
        }
    }

    private async Task OnPageSizeChangedAsync(int value)
    {
        currentPageSize = value;
        await GridPrefs.SetPageSizeAsync(GridKey, value);
    }

    private Task OnStationFilterChangedAsync(Guid? value)
    {
        stationFilter = value;
        return ReloadGridAsync();
    }

    private Task OnManpowerFilterChangedAsync(Guid? value)
    {
        manpowerFilter = value;
        return ReloadGridAsync();
    }

    private Task OnLicenseFilterChangedAsync(Guid? value)
    {
        licenseFilter = value;
        return ReloadGridAsync();
    }

    private Task ClearFiltersAsync()
    {
        stationFilter = manpowerFilter = licenseFilter = null;
        return ReloadGridAsync();
    }

    private static string StationLabel(StaffAllocationStation station) =>
        $"{station.IataCode} · {(string.IsNullOrWhiteSpace(station.City) ? station.Name : station.City)}";

    private Guid SelectedStationId(StaffAllocationMember member) =>
        pendingMove is { } move && move.Member.Id == member.Id ? move.Destination.Id : member.StationId;

    private void RenderStaffRow(RowRenderEventArgs<StaffAllocationMember> args)
    {
        if (pendingMove is { } move && move.Member.Id == args.Data?.Id)
            args.Attributes["class"] = "sa-staff-row-pending";
    }

    private async Task SelectDestinationAsync(StaffAllocationMember member, ChangeEventArgs args)
    {
        if (list is null || !CanReassignStaff || isLoading || isMoving || loadError
            || !Guid.TryParse(args.Value?.ToString(), out var stationId)
            || !stationsById.TryGetValue(stationId, out var destination)
            || !stationsById.TryGetValue(member.StationId, out var origin))
            return;

        moveSuccess = moveError = null;
        if (origin.Id == destination.Id)
        {
            await CancelMoveAsync();
            return;
        }

        if (pendingMove?.Member.Id != member.Id)
            await CancelMoveAsync();

        pendingMove = new StationMove(member, origin, destination, CoverageWarnings(member));
        await list.OpenRowDetailAsync(member);
    }

    private IReadOnlyList<string> CoverageWarnings(StaffAllocationMember member)
    {
        var coworkers = staff.Where(other => other.StationId == member.StationId && other.Id != member.Id).ToArray();
        var warnings = new List<string>();
        if (!coworkers.Any(other => other.ManpowerTypeId == member.ManpowerTypeId))
            warnings.Add(member.ManpowerTypeName);

        var coveredLicenses = coworkers.SelectMany(other => other.Licenses).Select(license => license.LicenseId).ToHashSet();
        warnings.AddRange(member.Licenses.Where(license => !coveredLicenses.Contains(license.LicenseId)).Select(license => license.Code));
        return warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task CancelMoveAsync()
    {
        if (pendingMove is not { } move)
            return;

        pendingMove = null;
        if (list is not null)
            await list.CloseRowDetailAsync(move.Member);
    }

    private async Task ConfirmMoveAsync()
    {
        if (pendingMove is not { } move || !CanReassignStaff || isLoading || isMoving || loadError)
            return;

        isMoving = true;
        try
        {
            try
            {
                await MasterData.ReassignStaffMemberStationAsync(move.Member.Id, move.Destination.Id, move.Member.RowVersion);
                moveSuccess = $"{move.Member.FullName} moved to {move.Destination.IataCode}.";
            }
            catch (ApiException ex)
            {
                moveError = ex.ProblemCode is "General.ConcurrencyConflict" or "MasterData.StaffMember.StationUnchanged"
                    ? "This employee’s assignment has changed. Review the current station and try again."
                    : ex.ToDisplayMessage("The staff member could not be moved.");
            }
            catch (HttpRequestException)
            {
                moveError = "The station change could not be confirmed. Review the current station before trying again.";
            }

            await CancelMoveAsync();
            await RefreshAsync();
            if (loadError && moveSuccess is not null)
            {
                moveError = $"{moveSuccess} The staff list could not be refreshed.";
                moveSuccess = null;
            }
        }
        finally
        {
            isMoving = false;
        }
    }

    private sealed record FilterOption(string Label, Guid Value);
    private sealed record StationMove(
        StaffAllocationMember Member,
        StaffAllocationStation Origin,
        StaffAllocationStation Destination,
        IReadOnlyList<string> CoverageWarnings);
}
