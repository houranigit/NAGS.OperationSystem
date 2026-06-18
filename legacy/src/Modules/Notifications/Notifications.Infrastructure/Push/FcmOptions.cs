namespace Notifications.Infrastructure.Push;

/// <summary>
/// Configuration for the Firebase-Admin push transport. Bound from <c>appsettings.json</c>
/// section <c>"Fcm"</c>; either <see cref="ServiceAccountJsonPath"/> (file on disk) or
/// <see cref="ServiceAccountJson"/> (literal JSON, e.g. from Key Vault) must be set when
/// <see cref="Enabled"/> is true.
/// </summary>
public sealed class FcmOptions
{
    public const string SectionName = "Fcm";

    /// <summary>
    /// When false, the FCM pusher is registered as a no-op decorator. Defaults to false so
    /// dev environments without a service account JSON don't crash on first push.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Absolute or relative path to the FirebaseAdmin service-account JSON.</summary>
    public string? ServiceAccountJsonPath { get; set; }

    /// <summary>
    /// Inline JSON for the service account (alternative to <see cref="ServiceAccountJsonPath"/>).
    /// Useful when the credential is supplied through an env var / secrets store.
    /// </summary>
    public string? ServiceAccountJson { get; set; }
}
