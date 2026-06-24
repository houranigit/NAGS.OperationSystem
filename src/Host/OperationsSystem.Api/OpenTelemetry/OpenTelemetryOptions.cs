namespace OperationsSystem.Api.OpenTelemetry;

public sealed class OpenTelemetryOptions
{
    public const string SectionName = "OpenTelemetry";

    public bool Enabled { get; set; } = true;

    public string ServiceName { get; set; } = "operations-system-api";

    /// <summary>
    /// OTLP endpoint (e.g. https://otel-collector:4317). When unset, standard
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> environment variables are honored by the exporter.
    /// </summary>
    public string? OtlpEndpoint { get; set; }
}
