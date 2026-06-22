using MasterData.Domain.StaffMembers;
using Shouldly;

namespace MasterData.Domain.UnitTests.StaffMembers;

public sealed class StaffMemberTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid StationId = Guid.NewGuid();
    private static readonly Guid ManpowerTypeId = Guid.NewGuid();

    private static StaffMember NewStaff(string email = "tech@example.com") =>
        StaffMember.Create("  Jane Technician ", $"  {email.ToUpperInvariant()} ", StationId, ManpowerTypeId, null, null, Now).Value;

    [Fact]
    public void Create_normalizes_name_and_email()
    {
        var result = StaffMember.Create("  Jane Technician ", "  TECH@Example.com ", StationId, ManpowerTypeId, null, null, Now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.FullName.ShouldBe("Jane Technician");
        result.Value.Email.ShouldBe("tech@example.com");
        result.Value.IsActive.ShouldBeTrue();
        result.Value.EmploymentContract.ShouldBeNull();
        result.Value.WorkingSchedule.ShouldBeNull();
    }

    [Fact]
    public void Create_with_blank_name_fails()
    {
        var result = StaffMember.Create("  ", "tech@example.com", StationId, ManpowerTypeId, null, null, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MasterData.StaffMember.NameRequired");
    }

    [Fact]
    public void Create_with_invalid_email_fails()
    {
        var result = StaffMember.Create("Jane", "not-an-email", StationId, ManpowerTypeId, null, null, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MasterData.StaffMember.EmailInvalid");
    }

    [Fact]
    public void Create_without_station_fails()
    {
        var result = StaffMember.Create("Jane", "tech@example.com", Guid.Empty, ManpowerTypeId, null, null, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MasterData.StaffMember.StationRequired");
    }

    [Fact]
    public void Create_without_manpower_type_fails()
    {
        var result = StaffMember.Create("Jane", "tech@example.com", StationId, Guid.Empty, null, null, Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MasterData.StaffMember.ManpowerTypeRequired");
    }

    [Fact]
    public void Create_persists_contract_and_schedule()
    {
        var contract = EmploymentContract.Create(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)).Value;
        var schedule = WorkingSchedule.Create([DayOfWeek.Sunday, DayOfWeek.Monday]).Value;

        var staff = StaffMember.Create("Jane", "tech@example.com", StationId, ManpowerTypeId, contract, schedule, Now).Value;

        staff.EmploymentStartDate.ShouldBe(new DateOnly(2026, 1, 1));
        staff.EmploymentEndDate.ShouldBe(new DateOnly(2026, 12, 31));
        staff.EmploymentContract.ShouldBe(contract);
        staff.WorkingSchedule!.Days.ShouldBe([DayOfWeek.Sunday, DayOfWeek.Monday]);
    }

    [Fact]
    public void Update_changes_fields_and_sets_timestamp()
    {
        var staff = NewStaff();
        var later = Now.AddDays(1);
        var newStation = Guid.NewGuid();

        var result = staff.Update("  John Lead ", " LEAD@example.com ", newStation, ManpowerTypeId, null, null, later);

        result.IsSuccess.ShouldBeTrue();
        staff.FullName.ShouldBe("John Lead");
        staff.Email.ShouldBe("lead@example.com");
        staff.StationId.ShouldBe(newStation);
        staff.UpdatedAtUtc.ShouldBe(later);
    }

    [Fact]
    public void Deactivate_then_activate_toggles_state()
    {
        var staff = NewStaff();

        staff.Deactivate(Now).IsSuccess.ShouldBeTrue();
        staff.IsActive.ShouldBeFalse();

        staff.Activate(Now).IsSuccess.ShouldBeTrue();
        staff.IsActive.ShouldBeTrue();
    }
}

public sealed class StaffMemberLicenseReconciliationTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static StaffMember NewStaff() =>
        StaffMember.Create("Jane", "tech@example.com", Guid.NewGuid(), Guid.NewGuid(), null, null, Now).Value;

    [Fact]
    public void Reconcile_adds_new_licenses()
    {
        var staff = NewStaff();
        var licenseId = Guid.NewGuid();

        var result = staff.ReconcileLicenses([new LicenseAssignmentItem(null, licenseId, "lic-1")], Now);

        result.IsSuccess.ShouldBeTrue();
        staff.Licenses.Count.ShouldBe(1);
        staff.Licenses[0].LicenseId.ShouldBe(licenseId);
        staff.Licenses[0].LicenseNumber.ShouldBe("LIC-1");
    }

    [Fact]
    public void Reconcile_updates_existing_and_removes_missing()
    {
        var staff = NewStaff();
        var licenseA = Guid.NewGuid();
        var licenseB = Guid.NewGuid();
        staff.ReconcileLicenses(
        [
            new LicenseAssignmentItem(null, licenseA, "a-1"),
            new LicenseAssignmentItem(null, licenseB, "b-1")
        ], Now);

        var keep = staff.Licenses.First(l => l.LicenseId == licenseA);

        var result = staff.ReconcileLicenses([new LicenseAssignmentItem(keep.Id, licenseA, "a-2")], Now);

        result.IsSuccess.ShouldBeTrue();
        staff.Licenses.Count.ShouldBe(1);
        staff.Licenses[0].Id.ShouldBe(keep.Id);
        staff.Licenses[0].LicenseNumber.ShouldBe("A-2");
    }

    [Fact]
    public void Reconcile_rejects_duplicate_license_type()
    {
        var staff = NewStaff();
        var licenseId = Guid.NewGuid();

        var result = staff.ReconcileLicenses(
        [
            new LicenseAssignmentItem(null, licenseId, "x-1"),
            new LicenseAssignmentItem(null, licenseId, "x-2")
        ], Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MasterData.StaffMemberLicense.DuplicateLicense");
    }

    [Fact]
    public void Reconcile_unknown_id_fails()
    {
        var staff = NewStaff();

        var result = staff.ReconcileLicenses([new LicenseAssignmentItem(Guid.NewGuid(), Guid.NewGuid(), "x-1")], Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MasterData.StaffMemberLicense.NotFound");
    }
}

public sealed class EmploymentContractTests
{
    [Fact]
    public void Create_allows_open_ended_contract()
    {
        var result = EmploymentContract.Create(new DateOnly(2026, 1, 1), null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.EndDate.ShouldBeNull();
    }

    [Fact]
    public void Create_rejects_end_before_start()
    {
        var result = EmploymentContract.Create(new DateOnly(2026, 6, 1), new DateOnly(2026, 1, 1));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MasterData.EmploymentContract.EndBeforeStart");
    }
}

public sealed class WorkingScheduleTests
{
    [Fact]
    public void Create_requires_at_least_one_day()
    {
        var result = WorkingSchedule.Create([]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MasterData.WorkingSchedule.Empty");
    }

    [Fact]
    public void Mask_round_trips()
    {
        var schedule = WorkingSchedule.Create([DayOfWeek.Sunday, DayOfWeek.Wednesday, DayOfWeek.Saturday]).Value;

        var restored = WorkingSchedule.FromMask(schedule.ToMask());

        restored.Days.ShouldBe([DayOfWeek.Sunday, DayOfWeek.Wednesday, DayOfWeek.Saturday]);
    }
}
