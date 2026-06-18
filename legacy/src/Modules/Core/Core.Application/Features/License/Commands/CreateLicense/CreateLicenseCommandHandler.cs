using BuildingBlocks.Application.Abstractions.Commands;
using BuildingBlocks.Domain.Results;
using Core.Domain.Aggregates.License;

namespace Core.Application.Features.License.Commands.CreateLicense;

/// <summary>
/// Creates a license (reference master data — no child collections; orchestration parallels <see cref="Core.Application.Features.Customer.Commands.CreateCustomer.CreateCustomerCommandHandler"/> without sync APIs).
/// </summary>
public sealed class CreateLicenseCommandHandler(ILicenseRepository licenses)
    : ICommandHandler<CreateLicenseCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateLicenseCommand request, CancellationToken cancellationToken)
    {
        if (await licenses.ExistsByCodeAsync(request.Code, cancellationToken))
            return Error.Conflict("A license with this code already exists.");

        var created = Core.Domain.Aggregates.License.License.Create(request.Code, request.Name, request.Description);
        if (created.IsFailure) return created.Error;

        var license = created.Value;

        if (!request.IsActive)
        {
            var d = license.Deactivate();
            if (d.IsFailure) return d.Error;
        }

        licenses.Add(license);
        return license.Id.Value;
    }
}
