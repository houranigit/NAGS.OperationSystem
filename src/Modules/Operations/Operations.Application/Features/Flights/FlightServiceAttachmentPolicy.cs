using BuildingBlocks.Domain.Results;
using Operations.Domain.ValueObjects;

namespace Operations.Application.Features.Flights;

/// <summary>Established attachment content rules for performed flight services.</summary>
public static class FlightServiceAttachmentPolicy
{
    public const int MaxImageBytes = 10 * 1024 * 1024;
    public const int MaxVoiceBytes = 25 * 1024 * 1024;
    public const int MaxDocumentBytes = 2 * 1024 * 1024;
    public const int MaxAggregateBytes = MaxVoiceBytes;
    public const int JsonEnvelopeAllowanceBytes = 4 * 1024 * 1024;
    public const long MaxJsonRequestBytes = (((long)MaxAggregateBytes + 2) / 3 * 4) + JsonEnvelopeAllowanceBytes;

    private static readonly HashSet<string> ImageContentTypes =
        new(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg", "image/webp" };

    private static readonly HashSet<string> VoiceContentTypes =
        new(StringComparer.OrdinalIgnoreCase) { "audio/mp4", "audio/m4a", "audio/mpeg", "audio/mp3", "audio/ogg", "audio/webm" };

    private static readonly HashSet<string> DocumentContentTypes =
        new(StringComparer.OrdinalIgnoreCase) { "application/pdf" };

    public static Result Validate(AttachmentKind kind, byte[] content, string fileName, string contentType)
    {
        if (!Enum.IsDefined(kind))
            return Error.Validation("Attachment kind is invalid.", "Operations.FlightService.AttachmentKindInvalid");
        if (content.Length == 0)
            return Error.Validation("The attachment file is empty.", "Operations.FlightService.AttachmentEmpty");
        if (content.Length > MaxBytes(kind))
            return Error.Validation($"The {kind.ToString().ToLowerInvariant()} attachment must be at most {MaxBytes(kind) / (1024 * 1024)} MB.", "Operations.FlightService.AttachmentTooLarge");

        var normalizedContentType = string.IsNullOrWhiteSpace(contentType) ? ContentTypeFromFileName(fileName) : contentType.Trim();
        if (!AllowedContentTypes(kind).Contains(normalizedContentType))
            return Error.Validation("The attachment file type is not allowed.", "Operations.FlightService.AttachmentInvalidType");
        if (!HasExpectedSignature(kind, normalizedContentType, content))
            return Error.Validation("The attachment content does not match the selected type.", "Operations.FlightService.AttachmentInvalidSignature");

        return Result.Success();
    }

    public static int MaxBytes(AttachmentKind kind) => kind switch
    {
        AttachmentKind.Image => MaxImageBytes,
        AttachmentKind.Voice => MaxVoiceBytes,
        AttachmentKind.Document => MaxDocumentBytes,
        _ => MaxDocumentBytes
    };

    private static HashSet<string> AllowedContentTypes(AttachmentKind kind) => kind switch
    {
        AttachmentKind.Image => ImageContentTypes,
        AttachmentKind.Voice => VoiceContentTypes,
        AttachmentKind.Document => DocumentContentTypes,
        _ => DocumentContentTypes
    };

    private static string ContentTypeFromFileName(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".m4a" => "audio/mp4",
        ".mp3" => "audio/mpeg",
        ".ogg" => "audio/ogg",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream"
    };

    private static bool HasExpectedSignature(AttachmentKind kind, string contentType, byte[] content) => kind switch
    {
        AttachmentKind.Image => HasImageSignature(contentType, content),
        AttachmentKind.Document => HasPdfSignature(content),
        AttachmentKind.Voice => HasVoiceSignature(contentType, content),
        _ => false
    };

    private static bool HasImageSignature(string contentType, byte[] content)
    {
        if (content.Length < 12)
            return false;
        if (contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase))
            return content[0] == 0x89 && content[1] == 0x50 && content[2] == 0x4E && content[3] == 0x47;
        if (contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase))
            return content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF;
        if (contentType.Equals("image/webp", StringComparison.OrdinalIgnoreCase))
            return content[0] == 0x52 && content[1] == 0x49 && content[2] == 0x46 && content[3] == 0x46
                && content[8] == 0x57 && content[9] == 0x45 && content[10] == 0x42 && content[11] == 0x50;

        return false;
    }

    private static bool HasPdfSignature(byte[] content) =>
        content.Length >= 5
        && content[0] == 0x25
        && content[1] == 0x50
        && content[2] == 0x44
        && content[3] == 0x46
        && content[4] == 0x2D;

    private static bool HasVoiceSignature(string contentType, byte[] content)
    {
        if (content.Length < 4)
            return false;
        if (contentType.Equals("audio/ogg", StringComparison.OrdinalIgnoreCase))
            return content[0] == 0x4F && content[1] == 0x67 && content[2] == 0x67 && content[3] == 0x53;
        if (contentType.Equals("audio/mpeg", StringComparison.OrdinalIgnoreCase) || contentType.Equals("audio/mp3", StringComparison.OrdinalIgnoreCase))
            return (content[0] == 0x49 && content[1] == 0x44 && content[2] == 0x33) || (content[0] == 0xFF && (content[1] & 0xE0) == 0xE0);
        if (contentType.Equals("audio/mp4", StringComparison.OrdinalIgnoreCase) || contentType.Equals("audio/m4a", StringComparison.OrdinalIgnoreCase))
            return content.Length >= 12 && content[4] == 0x66 && content[5] == 0x74 && content[6] == 0x79 && content[7] == 0x70;
        if (contentType.Equals("audio/webm", StringComparison.OrdinalIgnoreCase))
            return content[0] == 0x1A && content[1] == 0x45 && content[2] == 0xDF && content[3] == 0xA3;

        return false;
    }
}
