using System.Reflection;

namespace Operations.Application;

/// <summary>Marker for assembly scanning (MediatR handlers, FluentValidation validators).</summary>
public static class AssemblyReference
{
    public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
}
