using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace Contracts.Architecture.Tests;

/// <summary>
/// Architectural guard rails for the Contracts module. Mirrors the project rules described in
/// the root CLAUDE.md so dependency drift is caught at CI time rather than at runtime.
/// </summary>
public sealed class ContractsArchitectureTests
{
    private static readonly Assembly DomainAssembly = typeof(global::Contracts.Domain.Aggregates.Contract.Contract).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(global::Contracts.Application.Abstractions.IContractsDbContext).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(global::Contracts.Infrastructure.Persistence.ContractsDbContext).Assembly;
    private static readonly Assembly ContractsAssembly = typeof(global::Contracts.Contracts.Contract.ContractDto).Assembly;

    [Fact]
    public void Domain_should_not_reference_other_module_layers()
    {
        var forbidden = new[]
        {
            "Contracts.Application",
            "Contracts.Infrastructure",
            "Contracts.Contracts",
            "Core.Application",
            "Core.Infrastructure",
            "Core.Contracts",
            "Operations.Application",
            "Operations.Infrastructure",
            "Operations.Contracts",
            "Operations.Domain"
        };

        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOnAll(forbidden)
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailure("Contracts.Domain", result.FailingTypeNames));
    }

    [Fact]
    public void Application_should_not_reference_infrastructure_or_other_module_internals()
    {
        // Cross-module access is restricted to *.Contracts assemblies (per CLAUDE.md).
        // Application may reference Core.Contracts but never Core.Application/Infrastructure
        // or any Operations layer.
        var forbidden = new[]
        {
            "Contracts.Infrastructure",
            "Core.Application",
            "Core.Infrastructure",
            "Operations.Application",
            "Operations.Domain",
            "Operations.Infrastructure"
        };

        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOnAll(forbidden)
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailure("Contracts.Application", result.FailingTypeNames));
    }

    [Fact]
    public void Infrastructure_should_not_reference_other_module_internals()
    {
        var forbidden = new[]
        {
            "Core.Application",
            "Core.Domain",
            "Core.Infrastructure",
            "Operations.Application",
            "Operations.Domain",
            "Operations.Infrastructure"
        };

        var result = Types.InAssembly(InfrastructureAssembly)
            .Should()
            .NotHaveDependencyOnAll(forbidden)
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailure("Contracts.Infrastructure", result.FailingTypeNames));
    }

    [Fact]
    public void Contracts_dto_assembly_should_not_reference_application_or_infrastructure()
    {
        // Contracts.Contracts intentionally references Contracts.Domain so DTOs can reuse the
        // module enumerations (matches the established Core/Operations DTO pattern). Application
        // and Infrastructure must remain off-limits so the contract surface stays consumable
        // from other modules.
        var forbidden = new[]
        {
            "Contracts.Application",
            "Contracts.Infrastructure"
        };

        var result = Types.InAssembly(ContractsAssembly)
            .Should()
            .NotHaveDependencyOnAll(forbidden)
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailure("Contracts.Contracts (DTOs)", result.FailingTypeNames));
    }

    private static string FormatFailure(string assemblyLabel, IEnumerable<string>? failingTypes)
    {
        var names = failingTypes is null ? "<none>" : string.Join(", ", failingTypes);
        return $"{assemblyLabel} broke its dependency rule. Offending types: {names}.";
    }
}
