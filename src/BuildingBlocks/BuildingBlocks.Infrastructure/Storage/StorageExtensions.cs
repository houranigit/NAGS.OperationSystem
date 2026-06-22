using BuildingBlocks.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Infrastructure.Storage;

public static class StorageExtensions
{
    public static IServiceCollection AddLocalFileStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FileStorageOptions>()
            .Bind(configuration.GetSection(FileStorageOptions.SectionName));

        services.TryAddSingleton<IFileStorage, LocalFileStorage>();
        return services;
    }
}
