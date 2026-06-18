using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Operations.Domain.Aggregates.WorkOrder;

namespace Operations.Infrastructure.Persistence;

public sealed class WorkOrderIdConverter() : ValueConverter<WorkOrderId, Guid>(
    id => id.Value,
    value => WorkOrderId.From(value));
