using BuildingBlocks.Application.Abstractions.Commands;
using Core.Contracts.Features.Employee;

namespace Core.Application.Features.Employee.Commands.CreateEmployee;

public sealed record CreateEmployeeCommand(
    string FullName,
    string Email,
    Guid ManpowerTypeId,
    Guid StationId,
    DateOnly? ContractFrom,
    DateOnly? ContractTo,
    IReadOnlySet<DayOfWeek> WorkingDays,
    bool CreateLinkedUser,
    IReadOnlyList<EmployeeLicenseInput> Licenses) : ICommand<Guid>;
