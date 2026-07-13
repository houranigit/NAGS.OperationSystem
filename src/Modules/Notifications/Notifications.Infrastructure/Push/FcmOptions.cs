namespace Notifications.Infrastructure.Push;

public sealed class FcmOptions
{
    public const string SectionName = "Notifications:Fcm";

    public bool Enabled { get; set; }
    public bool Required { get; set; }
    public string? ProjectId { get; set; }
    public string? ServiceAccountJsonPath { get; set; }
    public string? ServiceAccountJson { get; set; }
}
