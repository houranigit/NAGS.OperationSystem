namespace Operations.Domain.Enumerations;

/// <summary>
/// Discriminator for a <see cref="Operations.Domain.Entities.WorkOrderTaskAttachment"/>.
/// Drives both UI rendering (image preview vs audio player vs document download link) and
/// the per-kind size-cap policy (see <c>WorkOrderTaskAttachment.Create</c>).
/// </summary>
public enum TaskAttachmentKind
{
    Image = 0,
    Voice = 1,
    Document = 2,
}
