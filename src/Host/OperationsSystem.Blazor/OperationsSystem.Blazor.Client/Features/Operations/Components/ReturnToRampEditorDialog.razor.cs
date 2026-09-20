using Microsoft.AspNetCore.Components.Forms;
using OperationsSystem.Blazor.Client.Api;

namespace OperationsSystem.Blazor.Client.Features.Operations.Components;

public partial class ReturnToRampEditorDialog
{
    private string? signaturePreviewUrl;

    private string? PendingSignaturePreview => model.PendingCustomerSignature is { } signature
        ? $"data:image/png;base64,{Convert.ToBase64String(signature.Content)}"
        : null;

    private async Task UploadSignatureAsync(InputFileChangeEventArgs args)
    {
        var file = args.File;
        if (file.Size <= 0 || file.Size > ReturnToRampSignatureValidation.MaxBytes)
        {
            errorMessage = "Customer signature must be a non-empty PNG image of at most 2 MB.";
            return;
        }
        if (!file.ContentType.Equals("image/png", StringComparison.OrdinalIgnoreCase) &&
            !file.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "Customer signature must be a PNG image.";
            return;
        }

        isAttachmentBusy = true;
        errorMessage = null;
        try
        {
            await using var stream = file.OpenReadStream(ReturnToRampSignatureValidation.MaxBytes);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            var signature = new ReturnToRampSignatureDraft
            {
                FileName = file.Name,
                ContentType = "image/png",
                Content = memory.ToArray()
            };
            if (ReturnToRampSignatureValidation.Validate(signature) is { } validationError)
            {
                errorMessage = validationError;
                return;
            }

            model.PendingCustomerSignature = signature;
            model.RemoveCustomerSignature = false;
            signaturePreviewUrl = null;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            errorMessage = "Could not read the customer signature.";
        }
        finally
        {
            isAttachmentBusy = false;
        }
    }

    private void ClearPendingSignature() => model.PendingCustomerSignature = null;

    private void RemoveSignature()
    {
        model.PendingCustomerSignature = null;
        model.RemoveCustomerSignature = true;
        signaturePreviewUrl = null;
    }

    private void KeepSavedSignature() => model.RemoveCustomerSignature = false;

    private async Task LoadSignatureAsync()
    {
        if (WorkOrderId is not { } workOrderId || model.Id is not { } returnToRampId)
            return;

        isAttachmentBusy = true;
        errorMessage = null;
        try
        {
            var file = await Operations.DownloadReturnToRampSignatureAsync(workOrderId, returnToRampId);
            signaturePreviewUrl = $"data:{file.ContentType};base64,{file.Base64}";
        }
        catch (ApiException ex)
        {
            errorMessage = ex.ToDisplayMessage("Could not load the RTR customer signature.");
        }
        finally
        {
            isAttachmentBusy = false;
        }
    }
}
