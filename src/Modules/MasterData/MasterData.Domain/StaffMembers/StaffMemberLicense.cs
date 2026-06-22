using BuildingBlocks.Domain.Entities;
using BuildingBlocks.Domain.Results;

namespace MasterData.Domain.StaffMembers;

/// <summary>
/// A license held by a <see cref="StaffMember"/>. Carries a stable assignment id, the referenced
/// License, and an uppercase license number. A staff member cannot hold the same License type twice.
/// Reconciled by stable assignment id.
/// </summary>
public sealed class StaffMemberLicense : Entity<Guid>
{
    private StaffMemberLicense() { }

    public Guid StaffMemberId { get; private set; }
    public Guid LicenseId { get; private set; }
    public string LicenseNumber { get; private set; } = null!;

    internal static Result<StaffMemberLicense> Create(Guid staffMemberId, Guid licenseId, string? licenseNumber, Guid? id = null)
    {
        if (licenseId == Guid.Empty)
            return Error.Validation("A license is required.", "MasterData.StaffMemberLicense.LicenseRequired");

        var numberCheck = NormalizeNumber(licenseNumber);
        if (numberCheck.IsFailure)
            return numberCheck.Error;

        return new StaffMemberLicense
        {
            Id = id ?? Guid.NewGuid(),
            StaffMemberId = staffMemberId,
            LicenseId = licenseId,
            LicenseNumber = numberCheck.Value
        };
    }

    internal Result Update(Guid licenseId, string? licenseNumber)
    {
        if (licenseId == Guid.Empty)
            return Error.Validation("A license is required.", "MasterData.StaffMemberLicense.LicenseRequired");

        var numberCheck = NormalizeNumber(licenseNumber);
        if (numberCheck.IsFailure)
            return numberCheck.Error;

        LicenseId = licenseId;
        LicenseNumber = numberCheck.Value;
        return Result.Success();
    }

    private static Result<string> NormalizeNumber(string? licenseNumber)
    {
        if (string.IsNullOrWhiteSpace(licenseNumber))
            return Error.Validation("A license number is required.", "MasterData.StaffMemberLicense.NumberRequired");

        var normalized = licenseNumber.Trim().ToUpperInvariant();
        if (normalized.Length > 100)
            return Error.Validation("License number must be at most 100 characters.", "MasterData.StaffMemberLicense.NumberTooLong");

        return normalized;
    }
}
