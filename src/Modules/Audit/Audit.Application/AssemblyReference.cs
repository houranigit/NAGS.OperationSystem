using System.Reflection;

namespace Audit.Application;

/// <summary>Marker used to register this assembly's MediatR handlers and FluentValidation validators.</summary>
public static class AssemblyReference
{
    public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
}
