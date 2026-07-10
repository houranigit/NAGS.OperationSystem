using Operations.Application.Features.WorkOrders;
using Operations.Domain.Enumerations;
using Shouldly;

namespace Operations.Application.UnitTests;

public sealed class WorkOrderAttachmentPolicyTests
{
    [Fact]
    public void Validate_AllowsExpectedAttachmentKinds()
    {
        WorkOrderAttachmentPolicy.Validate(TaskAttachmentKind.Document, [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31], "report.pdf", "application/pdf").IsSuccess.ShouldBeTrue();
        WorkOrderAttachmentPolicy.Validate(TaskAttachmentKind.Image, [0xFF, 0xD8, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00], "photo.jpg", "image/jpeg").IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Validate_RejectsMismatchedSignature()
    {
        var result = WorkOrderAttachmentPolicy.Validate(
            TaskAttachmentKind.Document,
            [0xFF, 0xD8, 0xFF, 0x00, 0x00],
            "fake.pdf",
            "application/pdf");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.WorkOrder.AttachmentInvalidSignature");
    }

    [Fact]
    public void SignaturePolicy_AllowsPngAndRejectsNonPngContent()
    {
        byte[] png =
        [
            0x89, 0x50, 0x4E, 0x47,
            0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00
        ];

        WorkOrderSignaturePolicy.Validate(png, "signature.png", "image/png").IsSuccess.ShouldBeTrue();

        var result = WorkOrderSignaturePolicy.Validate([0x25, 0x50, 0x44, 0x46], "signature.png", "image/png");

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Operations.WorkOrder.SignatureInvalidSignature");
    }
}
