using BuildingBlocks.Application.Abstractions;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Application.Persistence;
using BuildingBlocks.Domain.Results;
using FluentValidation;
using MasterData.Application.Abstractions;
using MasterData.Application.Authorization;
using MasterData.Domain.Customers;
using Microsoft.EntityFrameworkCore;

namespace MasterData.Application.Features.Customers;

// --- Shared input shapes --------------------------------------------------

public sealed record CustomerAddressInput(string? Line1, string? Line2, string? City, string? Region, string? PostalCode);

public sealed record CustomerContactInput(Guid? Id, string? Name, string? JobTitle, string? Email, string? Phone);

// --- Create ---------------------------------------------------------------

public sealed record CreateCustomerCommand(
    string IataCode,
    string? IcaoCode,
    string Name,
    Guid CountryId,
    string? OfficialEmail,
    string? OfficialPhone,
    CustomerAddressInput Address,
    IReadOnlyList<CustomerContactInput> Contacts) : ICommand<Guid>;

public sealed class CreateCustomerCommandValidator : AbstractValidator<CreateCustomerCommand>
{
    public CreateCustomerCommandValidator()
    {
        RuleFor(x => x.IataCode).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CountryId).NotEmpty();
        RuleFor(x => x.Address).NotNull();
    }
}

public sealed class CreateCustomerCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<CreateCustomerCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateCustomerCommand request, CancellationToken cancellationToken)
    {
        var countryCheck = await CustomerGuards.EnsureActiveCountryAsync(db, request.CountryId, cancellationToken);
        if (countryCheck.IsFailure)
            return countryCheck.Error;

        var addressResult = Address.Create(
            request.Address.Line1, request.Address.Line2, request.Address.City, request.Address.Region, request.Address.PostalCode);
        if (addressResult.IsFailure)
            return addressResult.Error;

        var now = timeProvider.GetUtcNow();
        var result = Customer.Create(
            request.IataCode, request.IcaoCode, request.Name, request.CountryId,
            request.OfficialEmail, request.OfficialPhone, logoFileReference: null, addressResult.Value, now);
        if (result.IsFailure)
            return result.Error;

        var customer = result.Value;

        var conflict = await CustomerGuards.EnsureCodesAvailableAsync(db, customer.IataCode, customer.IcaoCode, null, cancellationToken);
        if (conflict.IsFailure)
            return conflict.Error;

        var reconcile = customer.ReconcileContacts(MapContacts(request.Contacts), now);
        if (reconcile.IsFailure)
            return reconcile.Error;

        db.Customers.Add(customer);
        await db.SaveChangesAsync(cancellationToken);
        return customer.Id;
    }

    internal static IReadOnlyList<ContactReconciliationItem> MapContacts(IReadOnlyList<CustomerContactInput>? contacts) =>
        contacts is null
            ? []
            : contacts.Select(c => new ContactReconciliationItem(c.Id, c.Name, c.JobTitle, c.Email, c.Phone)).ToList();
}

// --- Update ---------------------------------------------------------------

public sealed record UpdateCustomerCommand(
    Guid Id,
    string IataCode,
    string? IcaoCode,
    string Name,
    Guid CountryId,
    string? OfficialEmail,
    string? OfficialPhone,
    CustomerAddressInput Address,
    byte[] RowVersion) : ICommand;

public sealed class UpdateCustomerCommandValidator : AbstractValidator<UpdateCustomerCommand>
{
    public UpdateCustomerCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.IataCode).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CountryId).NotEmpty();
        RuleFor(x => x.Address).NotNull();
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class UpdateCustomerCommandHandler(IMasterDataDbContext db, IMasterDataScope scope, TimeProvider timeProvider)
    : ICommandHandler<UpdateCustomerCommand>
{
    public async Task<Result> Handle(UpdateCustomerCommand request, CancellationToken cancellationToken)
    {
        var scopeCheck = await scope.CheckCustomerAsync(request.Id, cancellationToken);
        if (scopeCheck.IsFailure)
            return scopeCheck.Error;

        // Customer update changes only customer fields. Contacts are managed through their own
        // dedicated add/update/remove endpoints (no reconciliation here).
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);
        if (customer is null)
            return Error.NotFound("Customer not found.", "MasterData.Customer.NotFound");

        var countryCheck = await CustomerGuards.EnsureActiveCountryAsync(db, request.CountryId, cancellationToken);
        if (countryCheck.IsFailure)
            return countryCheck.Error;

        var addressResult = Address.Create(
            request.Address.Line1, request.Address.Line2, request.Address.City, request.Address.Region, request.Address.PostalCode);
        if (addressResult.IsFailure)
            return addressResult.Error;

        var now = timeProvider.GetUtcNow();
        var result = customer.Update(
            request.IataCode, request.IcaoCode, request.Name, request.CountryId,
            request.OfficialEmail, request.OfficialPhone, addressResult.Value, now);
        if (result.IsFailure)
            return result.Error;

        var conflict = await CustomerGuards.EnsureCodesAvailableAsync(db, customer.IataCode, customer.IcaoCode, customer.Id, cancellationToken);
        if (conflict.IsFailure)
            return conflict.Error;

        db.SetOriginalRowVersion(customer, request.RowVersion);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }

        return Result.Success();
    }
}

// --- Add contact ----------------------------------------------------------

public sealed record AddCustomerContactCommand(Guid CustomerId, string? Name, string? JobTitle, string? Email, string? Phone, byte[] RowVersion) : ICommand<Guid>;

public sealed class AddCustomerContactCommandValidator : AbstractValidator<AddCustomerContactCommand>
{
    public AddCustomerContactCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Email).NotEmpty();
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class AddCustomerContactCommandHandler(IMasterDataDbContext db, IMasterDataScope scope, TimeProvider timeProvider)
    : ICommandHandler<AddCustomerContactCommand, Guid>
{
    public async Task<Result<Guid>> Handle(AddCustomerContactCommand request, CancellationToken cancellationToken)
    {
        var scopeCheck = await scope.CheckCustomerAsync(request.CustomerId, cancellationToken);
        if (scopeCheck.IsFailure)
            return scopeCheck.Error;

        var customer = await db.Customers
            .Include(c => c.Contacts)
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken);
        if (customer is null)
            return Error.NotFound("Customer not found.", "MasterData.Customer.NotFound");

        var added = customer.AddContact(request.Name, request.JobTitle, request.Email, request.Phone, timeProvider.GetUtcNow());
        if (added.IsFailure)
            return added.Error;

        db.SetOriginalRowVersion(customer, request.RowVersion);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }

        return added.Value.Id;
    }
}

// --- Update contact -------------------------------------------------------

public sealed record UpdateCustomerContactCommand(Guid CustomerId, Guid ContactId, string? Name, string? JobTitle, string? Email, string? Phone, byte[] RowVersion) : ICommand;

public sealed class UpdateCustomerContactCommandValidator : AbstractValidator<UpdateCustomerContactCommand>
{
    public UpdateCustomerContactCommandValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.ContactId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty();
        RuleFor(x => x.Email).NotEmpty();
        RuleFor(x => x.RowVersion).NotEmpty();
    }
}

public sealed class UpdateCustomerContactCommandHandler(IMasterDataDbContext db, IMasterDataScope scope, TimeProvider timeProvider)
    : ICommandHandler<UpdateCustomerContactCommand>
{
    public async Task<Result> Handle(UpdateCustomerContactCommand request, CancellationToken cancellationToken)
    {
        var scopeCheck = await scope.CheckCustomerAsync(request.CustomerId, cancellationToken);
        if (scopeCheck.IsFailure)
            return scopeCheck.Error;

        var customer = await db.Customers
            .Include(c => c.Contacts)
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken);
        if (customer is null)
            return Error.NotFound("Customer not found.", "MasterData.Customer.NotFound");

        var result = customer.UpdateContact(request.ContactId, request.Name, request.JobTitle, request.Email, request.Phone, timeProvider.GetUtcNow());
        if (result.IsFailure)
            return result.Error;

        db.SetOriginalRowVersion(customer, request.RowVersion);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }

        return Result.Success();
    }
}

// --- Logo -----------------------------------------------------------------

public sealed record SetCustomerLogoCommand(Guid Id, byte[] Content, string FileName, string ContentType, byte[] RowVersion) : ICommand<string>;

public sealed class SetCustomerLogoCommandHandler(IMasterDataDbContext db, IMasterDataScope scope, IFileStorage storage, TimeProvider timeProvider)
    : ICommandHandler<SetCustomerLogoCommand, string>
{
    // Allow-list of raster image types for customer logos.
    private static readonly HashSet<string> AllowedContentTypes =
        new(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg", "image/webp", "image/gif" };

    public async Task<Result<string>> Handle(SetCustomerLogoCommand request, CancellationToken cancellationToken)
    {
        // Logo upload is a customer sub-operation and must honor the caller's data scope.
        var scopeCheck = await scope.CheckCustomerAsync(request.Id, cancellationToken);
        if (scopeCheck.IsFailure)
            return scopeCheck.Error;

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);
        if (customer is null)
            return Error.NotFound("Customer not found.", "MasterData.Customer.NotFound");

        if (request.Content.Length == 0)
            return Error.Validation("The logo file is empty.", "MasterData.Customer.LogoEmpty");

        if (request.Content.Length > 2 * 1024 * 1024)
            return Error.Validation("The logo file must be at most 2 MB.", "MasterData.Customer.LogoTooLarge");

        if (!AllowedContentTypes.Contains(request.ContentType) || !HasImageSignature(request.Content))
            return Error.Validation("The logo must be a PNG, JPEG, WEBP, or GIF image.", "MasterData.Customer.LogoInvalidType");

        var previousKey = customer.LogoFileReference;

        using var stream = new MemoryStream(request.Content);
        var stored = await storage.SaveAsync("customer-logos", request.FileName, request.ContentType, stream, cancellationToken);

        customer.SetLogo(stored.StorageKey, timeProvider.GetUtcNow());
        db.SetOriginalRowVersion(customer, request.RowVersion);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await storage.DeleteAsync(stored.StorageKey, cancellationToken);
            return ConcurrencyErrors.Stale;
        }

        if (!string.IsNullOrWhiteSpace(previousKey))
            await storage.DeleteAsync(previousKey, cancellationToken);

        return stored.StorageKey;
    }

    /// <summary>Verifies the bytes start with a known raster-image magic number (defends against a spoofed content type).</summary>
    private static bool HasImageSignature(byte[] content)
    {
        if (content.Length < 12)
            return false;

        // PNG
        if (content[0] == 0x89 && content[1] == 0x50 && content[2] == 0x4E && content[3] == 0x47)
            return true;
        // JPEG
        if (content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
            return true;
        // GIF ("GIF8")
        if (content[0] == 0x47 && content[1] == 0x49 && content[2] == 0x46 && content[3] == 0x38)
            return true;
        // WEBP ("RIFF"...."WEBP")
        if (content[0] == 0x52 && content[1] == 0x49 && content[2] == 0x46 && content[3] == 0x46
            && content[8] == 0x57 && content[9] == 0x45 && content[10] == 0x42 && content[11] == 0x50)
            return true;

        return false;
    }
}

// --- Activate / Deactivate ------------------------------------------------

public sealed record ActivateCustomerCommand(Guid Id, byte[] RowVersion) : ICommand;

public sealed record DeactivateCustomerCommand(Guid Id, byte[] RowVersion) : ICommand;

public sealed class ActivateCustomerCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<ActivateCustomerCommand>
{
    public async Task<Result> Handle(ActivateCustomerCommand request, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);
        if (customer is null)
            return Error.NotFound("Customer not found.", "MasterData.Customer.NotFound");

        customer.Activate(timeProvider.GetUtcNow());
        db.SetOriginalRowVersion(customer, request.RowVersion);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }

        return Result.Success();
    }
}

public sealed class DeactivateCustomerCommandHandler(IMasterDataDbContext db, TimeProvider timeProvider)
    : ICommandHandler<DeactivateCustomerCommand>
{
    public async Task<Result> Handle(DeactivateCustomerCommand request, CancellationToken cancellationToken)
    {
        var customer = await db.Customers
            .Include(c => c.Contacts)
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);
        if (customer is null)
            return Error.NotFound("Customer not found.", "MasterData.Customer.NotFound");

        if (customer.IsActive)
        {
            // Deactivating a customer blocks access for all of its linked contacts.
            foreach (var contact in customer.Contacts.Where(c => c.IsActive && c.LinkedUserId is not null))
                PortalAccess.PortalLifecycle.EnqueueDeactivation(db, contact.Id, contact.LinkedUserId!.Value);
        }

        customer.Deactivate(timeProvider.GetUtcNow());
        db.SetOriginalRowVersion(customer, request.RowVersion);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrencyErrors.Stale;
        }

        return Result.Success();
    }
}

internal static class CustomerGuards
{
    public static async Task<Result> EnsureActiveCountryAsync(IMasterDataDbContext db, Guid countryId, CancellationToken cancellationToken)
    {
        var country = await db.Countries.FirstOrDefaultAsync(c => c.Id == countryId, cancellationToken);
        if (country is null)
            return Error.NotFound("The selected country was not found.", "MasterData.Customer.CountryNotFound");

        if (!country.IsActive)
            return Error.Validation("The selected country is inactive.", "MasterData.Customer.CountryInactive");

        return Result.Success();
    }

    public static async Task<Result> EnsureCodesAvailableAsync(
        IMasterDataDbContext db, string iataCode, string? icaoCode, Guid? excludeId, CancellationToken cancellationToken)
    {
        var iataTaken = await db.Customers.AnyAsync(c => c.IataCode == iataCode && (excludeId == null || c.Id != excludeId), cancellationToken);
        if (iataTaken)
            return Error.Conflict("A customer with this IATA code already exists.", "MasterData.Customer.DuplicateIata");

        if (icaoCode is not null)
        {
            var icaoTaken = await db.Customers.AnyAsync(c => c.IcaoCode == icaoCode && (excludeId == null || c.Id != excludeId), cancellationToken);
            if (icaoTaken)
                return Error.Conflict("A customer with this ICAO code already exists.", "MasterData.Customer.DuplicateIcao");
        }

        return Result.Success();
    }
}
