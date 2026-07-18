using Microsoft.AspNetCore.Components;
using OperationsSystem.Blazor.Client.Api;
using OperationsSystem.Blazor.Client.Auth;
using Radzen;

namespace OperationsSystem.Blazor.Client.Features.Operations.Pages;

public partial class StaffAllocationPage
{
    [Inject] private MasterDataApiClient MasterData { get; set; } = default!;
    [Inject] private AuthSession Auth { get; set; } = default!;
    [Inject] private DialogService Dialogs { get; set; } = default!;
    [Inject] private NotificationService Notifications { get; set; } = default!;

    private StaffAllocationOverview? overview;
    private Guid? selectedStationId;
    private Guid? selectedMemberId;
    private Guid? destinationStationId;
    private Guid? manpowerFilter;
    private Guid? licenseFilter;
    private string searchTerm = string.Empty;
    private bool isLoading = true;
    private bool loadError;
    private bool isMoving;

    private bool CanMoveStaff => Auth.HasPermission(MasterDataPermissions.StaffMembersUpdate);
    private bool HasFilters => manpowerFilter is not null || licenseFilter is not null || !string.IsNullOrWhiteSpace(searchTerm);

    private int TotalManpowerTypes => overview?.StaffMembers
        .Select(member => member.ManpowerTypeId)
        .Distinct()
        .Count() ?? 0;

    private int TotalLicenseTypes => overview?.StaffMembers
        .SelectMany(member => member.Licenses)
        .Select(license => license.LicenseId)
        .Distinct()
        .Count() ?? 0;

    private StaffAllocationStation? SelectedStation => overview?.Stations
        .FirstOrDefault(station => station.Id == selectedStationId);

    private StaffAllocationMember? SelectedMember => overview?.StaffMembers
        .FirstOrDefault(member => member.Id == selectedMemberId);

    private StaffAllocationStation? SelectedDestination => overview?.Stations
        .FirstOrDefault(station => station.Id == destinationStationId);

    private IEnumerable<StaffAllocationMember> FilteredCandidates => selectedStationId is not { } stationId
        ? []
        : StationMatchingStaff(stationId)
            .OrderBy(member => member.FullName)
            .ThenBy(member => member.Id);

    private IReadOnlyList<FilterOption> ManpowerOptions => overview?.StaffMembers
        .GroupBy(member => member.ManpowerTypeId)
        .Select(group => new FilterOption(group.First().ManpowerTypeName, group.Key))
        .OrderBy(option => option.Label)
        .ToList() ?? [];

    private IReadOnlyList<FilterOption> LicenseOptions => overview?.StaffMembers
        .SelectMany(member => member.Licenses)
        .GroupBy(license => license.LicenseId)
        .Select(group => new FilterOption($"{group.First().Code} · {group.First().Name}", group.Key))
        .OrderBy(option => option.Label)
        .ToList() ?? [];

    private IReadOnlyList<FilterOption> DestinationOptions => overview?.Stations
        .Where(station => station.Id != selectedStationId)
        .Select(station => new FilterOption($"{station.IataCode} · {station.Name}", station.Id))
        .OrderBy(option => option.Label)
        .ToList() ?? [];

    private string CandidateSummary
    {
        get
        {
            var total = selectedStationId is { } stationId ? StationStaff(stationId).Count : 0;
            if (!HasFilters)
                return $"{total} active {(total == 1 ? "employee" : "employees")} available to review";

            var matching = FilteredCandidates.Count();
            return $"{matching} of {total} employees match the current filters";
        }
    }

    private IReadOnlyList<string> SourceCoverageWarnings
    {
        get
        {
            if (SelectedMember is not { } member || SelectedStation is not { } station)
                return [];

            var warnings = new List<string>();
            if (ManpowerHolderCount(station.Id, member.ManpowerTypeId) == 1)
                warnings.Add(member.ManpowerTypeName);

            warnings.AddRange(member.Licenses
                .Where(license => LicenseHolderCount(station.Id, license.LicenseId) == 1)
                .Select(license => license.Code));

            return warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    protected override async Task OnInitializedAsync()
    {
        if (Auth.IsSystemAdministrator && CanMoveStaff)
            await LoadAsync();
        else
            isLoading = false;
    }

    private async Task LoadAsync()
    {
        isLoading = true;
        loadError = false;

        try
        {
            var result = await MasterData.GetStaffAllocationAsync();
            overview = result;

            if (selectedStationId is null || result.Stations.All(station => station.Id != selectedStationId))
            {
                selectedStationId = result.Stations
                    .OrderByDescending(station => result.StaffMembers.Count(member => member.StationId == station.Id))
                    .ThenBy(station => station.IataCode)
                    .Select(station => (Guid?)station.Id)
                    .FirstOrDefault();
            }

            if (selectedMemberId is { } memberId)
            {
                var refreshedMember = result.StaffMembers.FirstOrDefault(member => member.Id == memberId);
                if (refreshedMember is null || refreshedMember.StationId != selectedStationId)
                {
                    selectedMemberId = null;
                    destinationStationId = null;
                }
            }

            if (destinationStationId is { } destinationId && result.Stations.All(station => station.Id != destinationId))
                destinationStationId = null;
        }
        catch (ApiException)
        {
            loadError = true;
        }
        finally
        {
            isLoading = false;
        }
    }

    private Task ReloadAsync() => LoadAsync();

    private IReadOnlyList<StaffAllocationMember> StationStaff(Guid stationId) => overview?.StaffMembers
        .Where(member => member.StationId == stationId)
        .ToList() ?? [];

    private IReadOnlyList<StaffAllocationMember> StationMatchingStaff(Guid stationId) => StationStaff(stationId)
        .Where(MatchesFilters)
        .ToList();

    private bool MatchesFilters(StaffAllocationMember member)
    {
        if (manpowerFilter is { } manpowerId && member.ManpowerTypeId != manpowerId)
            return false;

        if (licenseFilter is { } licenseId && member.Licenses.All(license => license.LicenseId != licenseId))
            return false;

        var term = searchTerm.Trim();
        if (term.Length == 0)
            return true;

        return member.FullName.Contains(term, StringComparison.OrdinalIgnoreCase)
               || member.EmployeeId.Contains(term, StringComparison.OrdinalIgnoreCase)
               || member.ManpowerTypeName.Contains(term, StringComparison.OrdinalIgnoreCase)
               || member.Licenses.Any(license =>
                   license.Code.Contains(term, StringComparison.OrdinalIgnoreCase)
                   || license.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                   || license.LicenseNumber.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private int StationManpowerCount(Guid stationId) => StationStaff(stationId)
        .Select(member => member.ManpowerTypeId)
        .Distinct()
        .Count();

    private int StationLicenseCount(Guid stationId) => StationStaff(stationId)
        .SelectMany(member => member.Licenses)
        .Select(license => license.LicenseId)
        .Distinct()
        .Count();

    private int ManpowerHolderCount(Guid stationId, Guid manpowerTypeId) => StationStaff(stationId)
        .Count(member => member.ManpowerTypeId == manpowerTypeId);

    private int LicenseHolderCount(Guid stationId, Guid licenseId) => StationStaff(stationId)
        .Count(member => member.Licenses.Any(license => license.LicenseId == licenseId));

    private IReadOnlyList<CapabilityCount> TopManpower(Guid stationId) => StationStaff(stationId)
        .GroupBy(member => member.ManpowerTypeName)
        .Select(group => new CapabilityCount(group.Key, group.Count()))
        .OrderByDescending(item => item.Count)
        .ThenBy(item => item.Name)
        .Take(3)
        .ToList();

    private void SelectStation(Guid stationId)
    {
        if (selectedStationId == stationId)
            return;

        selectedStationId = stationId;
        selectedMemberId = null;
        destinationStationId = null;
    }

    private void SelectMember(Guid memberId)
    {
        selectedMemberId = memberId;
        destinationStationId = null;
    }

    private void ClearFilters()
    {
        searchTerm = string.Empty;
        manpowerFilter = null;
        licenseFilter = null;
    }

    private string StationCardClass(Guid stationId) =>
        $"sa-station-card{(selectedStationId == stationId ? " sa-station-card--selected" : string.Empty)}";

    private string CandidateCardClass(Guid memberId) =>
        $"sa-candidate{(selectedMemberId == memberId ? " sa-candidate--selected" : string.Empty)}";

    private async Task ReviewAndMoveAsync()
    {
        if (SelectedMember is not { } member
            || SelectedStation is not { } source
            || SelectedDestination is not { } destination
            || isMoving)
        {
            return;
        }

        var warning = SourceCoverageWarnings.Count == 0
            ? string.Empty
            : $" Warning: {source.IataCode} will have no remaining holder for {string.Join(", ", SourceCoverageWarnings)}.";

        var confirmed = await Dialogs.Confirm(
            $"Move {member.FullName} from {source.IataCode} to {destination.IataCode}? This permanently changes the employee's primary station.{warning}",
            "Confirm staff reassignment",
            new ConfirmOptions
            {
                OkButtonText = "Move staff member",
                CancelButtonText = "Keep current station"
            }) ?? false;

        if (!confirmed)
            return;

        isMoving = true;
        try
        {
            await MasterData.ReassignStaffMemberStationAsync(member.Id, destination.Id, member.RowVersion);
            selectedStationId = destination.Id;
            selectedMemberId = null;
            destinationStationId = null;
            await LoadAsync();
            Notifications.Notify(NotificationSeverity.Success, $"{member.FullName} moved to {destination.IataCode}.");
        }
        catch (ApiException ex)
        {
            Notifications.Notify(
                NotificationSeverity.Error,
                ex.ToDisplayMessage("The staff member could not be moved. Refresh and try again."));
        }
        finally
        {
            isMoving = false;
        }
    }

    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => string.Concat(parts[0][..1], parts[^1][..1]).ToUpperInvariant()
        };
    }

    private sealed record FilterOption(string Label, Guid? Value);
    private sealed record CapabilityCount(string Name, int Count);
}
