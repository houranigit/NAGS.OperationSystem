using Operations.Application.Features.Flights;
using Operations.Domain.ValueObjects;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class FlightServiceAttachmentPolicyTests
{
    [Theory]
    [InlineData(AttachmentKind.Image, 10 * 1024 * 1024, true)]
    [InlineData(AttachmentKind.Image, 10 * 1024 * 1024 + 1, false)]
    [InlineData(AttachmentKind.Voice, 25 * 1024 * 1024, true)]
    [InlineData(AttachmentKind.Voice, 25 * 1024 * 1024 + 1, false)]
    [InlineData(AttachmentKind.Document, 2 * 1024 * 1024, true)]
    [InlineData(AttachmentKind.Document, 2 * 1024 * 1024 + 1, false)]
    public void Validate_EnforcesTheEstablishedPerKindByteLimits(AttachmentKind kind, int size, bool allowed)
    {
        var content = new byte[size];
        Header(kind).CopyTo(content, 0);
        var (fileName, contentType) = Metadata(kind);

        var result = FlightServiceAttachmentPolicy.Validate(kind, content, fileName, contentType);

        result.IsSuccess.ShouldBe(allowed);
        if (!allowed)
            result.Error.Code.ShouldBe("Operations.FlightService.AttachmentTooLarge");
    }

    [Theory]
    [MemberData(nameof(AllowedFormats))]
    public void Validate_AcceptsEstablishedMimeTypesWithMatchingHeaders(
        AttachmentKind kind, string fileName, string contentType, byte[] content)
    {
        FlightServiceAttachmentPolicy.Validate(kind, content, fileName, contentType).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(AttachmentKind.Image, "photo.PNG")]
    [InlineData(AttachmentKind.Voice, "voice.M4A")]
    [InlineData(AttachmentKind.Document, "report.PDF")]
    public void Validate_UsesTheFilenameMimeFallbackWhenContentTypeIsBlank(AttachmentKind kind, string fileName)
    {
        FlightServiceAttachmentPolicy.Validate(kind, Header(kind), fileName, " \t ").IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(AttachmentKind.Image)]
    [InlineData(AttachmentKind.Voice)]
    [InlineData(AttachmentKind.Document)]
    public void Validate_RejectsEmptyContent(AttachmentKind kind)
    {
        var (fileName, contentType) = Metadata(kind);

        var result = FlightServiceAttachmentPolicy.Validate(kind, [], fileName, contentType);

        result.Error.Code.ShouldBe("Operations.FlightService.AttachmentEmpty");
    }

    [Theory]
    [InlineData(AttachmentKind.Image, "application/pdf")]
    [InlineData(AttachmentKind.Voice, "image/png")]
    [InlineData(AttachmentKind.Document, "audio/mp4")]
    [InlineData(AttachmentKind.Image, "image/gif")]
    [InlineData(AttachmentKind.Voice, "audio/wav")]
    [InlineData(AttachmentKind.Document, "application/msword")]
    public void Validate_RejectsCrossKindAndUnsupportedMimeTypes(AttachmentKind kind, string contentType)
    {
        var result = FlightServiceAttachmentPolicy.Validate(kind, Header(kind), "evidence.bin", contentType);

        result.Error.Code.ShouldBe("Operations.FlightService.AttachmentInvalidType");
    }

    [Theory]
    [MemberData(nameof(MismatchedSignatures))]
    public void Validate_RejectsMimeTypesWhoseContentHeaderDoesNotMatch(
        AttachmentKind kind, string fileName, string contentType, byte[] content)
    {
        var result = FlightServiceAttachmentPolicy.Validate(kind, content, fileName, contentType);

        result.Error.Code.ShouldBe("Operations.FlightService.AttachmentInvalidSignature");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(99)]
    public void Validate_RejectsUndefinedAttachmentKindsEvenForValidPdfContent(int value)
    {
        var result = FlightServiceAttachmentPolicy.Validate((AttachmentKind)value, Header(AttachmentKind.Document),
            "report.pdf", "application/pdf");

        result.Error.Code.ShouldBe("Operations.FlightService.AttachmentKindInvalid");
    }

    public static TheoryData<AttachmentKind, string, string, byte[]> AllowedFormats => new()
    {
        { AttachmentKind.Image, "photo.png", "image/png", Header(AttachmentKind.Image) },
        { AttachmentKind.Image, "photo.jpg", " Image/JPEG ", [0xFF, 0xD8, 0xFF, 0, 0, 0, 0, 0, 0, 0, 0, 0] },
        { AttachmentKind.Image, "photo.webp", "image/webp", [0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50] },
        { AttachmentKind.Voice, "voice.m4a", "audio/mp4", Header(AttachmentKind.Voice) },
        { AttachmentKind.Voice, "voice.m4a", "audio/m4a", Header(AttachmentKind.Voice) },
        { AttachmentKind.Voice, "voice.mp3", "audio/mpeg", [0x49, 0x44, 0x33, 0] },
        { AttachmentKind.Voice, "voice.mp3", "audio/mp3", [0xFF, 0xFB, 0, 0] },
        { AttachmentKind.Voice, "voice.ogg", "audio/ogg", [0x4F, 0x67, 0x67, 0x53] },
        { AttachmentKind.Voice, "voice.webm", "audio/webm", [0x1A, 0x45, 0xDF, 0xA3] },
        { AttachmentKind.Document, "report.pdf", "application/pdf", Header(AttachmentKind.Document) }
    };

    public static TheoryData<AttachmentKind, string, string, byte[]> MismatchedSignatures => new()
    {
        { AttachmentKind.Image, "fake.png", "image/png", [0xFF, 0xD8, 0xFF, 0, 0, 0, 0, 0, 0, 0, 0, 0] },
        { AttachmentKind.Image, "fake.jpg", "image/jpeg", Header(AttachmentKind.Image) },
        { AttachmentKind.Image, "fake.webp", "image/webp", [0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x41, 0x56, 0x45] },
        { AttachmentKind.Voice, "fake.m4a", "audio/mp4", [0x1A, 0x45, 0xDF, 0xA3, 0, 0, 0, 0, 0, 0, 0, 0] },
        { AttachmentKind.Voice, "fake.mp3", "audio/mpeg", [0x4F, 0x67, 0x67, 0x53] },
        { AttachmentKind.Voice, "fake.ogg", "audio/ogg", [0x49, 0x44, 0x33, 0] },
        { AttachmentKind.Voice, "fake.webm", "audio/webm", [0, 0, 0, 0, 0x57, 0x45, 0x42, 0x4D] },
        { AttachmentKind.Document, "fake.pdf", "application/pdf", [0x25, 0x50, 0x44, 0x46, 0] }
    };

    private static byte[] Header(AttachmentKind kind) => kind switch
    {
        AttachmentKind.Image => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0],
        AttachmentKind.Voice => [0, 0, 0, 0x18, 0x66, 0x74, 0x79, 0x70, 0x6D, 0x70, 0x34, 0x32],
        _ => [0x25, 0x50, 0x44, 0x46, 0x2D]
    };

    private static (string FileName, string ContentType) Metadata(AttachmentKind kind) => kind switch
    {
        AttachmentKind.Image => ("photo.png", "image/png"),
        AttachmentKind.Voice => ("voice.m4a", "audio/mp4"),
        _ => ("report.pdf", "application/pdf")
    };
}
