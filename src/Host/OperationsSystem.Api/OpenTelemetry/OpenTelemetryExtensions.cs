using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace OperationsSystem.Api.OpenTelemetry;

public static class OpenTelemetryExtensions
{
    public static IServiceCollection AddOperationsOpenTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection(OpenTelemetryOptions.SectionName).Get<OpenTelemetryOptions>()
                      ?? new OpenTelemetryOptions();

        if (!options.Enabled)
            return services;

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(options.ServiceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(instrumentation =>
                    {
                        instrumentation.Filter = context =>
                            !context.Request.Path.StartsWithSegments("/health");
                    })
                    .AddHttpClientInstrumentation();

                ConfigureOtlpExporter(tracing, options.OtlpEndpoint);
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                ConfigureOtlpExporter(metrics, options.OtlpEndpoint);
            });

        return services;
    }

    private static void ConfigureOtlpExporter(TracerProviderBuilder builder, string? endpoint) =>
        builder.AddOtlpExporter(otlp =>
        {
            if (!string.IsNullOrWhiteSpace(endpoint))
                otlp.Endpoint = new Uri(endpoint);
        });

    private static void ConfigureOtlpExporter(MeterProviderBuilder builder, string? endpoint) =>
        builder.AddOtlpExporter(otlp =>
        {
            if (!string.IsNullOrWhiteSpace(endpoint))
                otlp.Endpoint = new Uri(endpoint);
        });
}
