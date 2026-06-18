using BuildingBlocks.Application.Behaviors;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Application.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBuildingBlocksApplication(
        this IServiceCollection services,
        params System.Reflection.Assembly[] assemblies)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblies(assemblies));
        services.AddScoped<IPublisher>(sp => sp.GetRequiredService<IMediator>());
        services.AddValidatorsFromAssemblies(assemblies);

        // Order matches the pipeline: Logging → Validation → Authorization → Transaction → Handler
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        return services;
    }
}
