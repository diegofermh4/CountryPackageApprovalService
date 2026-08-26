namespace CountryPackageApprovalService.Domain;

/// <summary>
/// A single upload attached to one <see cref="ApprovalStep"/>. Versions are scoped per step (never shared
/// across steps), so "the document may be revised in subsequent obtain-decision steps, but documents attached
/// to previously completed steps must remain unchanged" falls out naturally: once the owning step locks,
/// every DocumentVersion under it becomes read-only (enforced by <see cref="ApprovalStep.AttachDocument"/> refusing
/// new versions on a locked step; reinforced by a storage-level immutability policy in the Azure target - see
/// docs/ARCHITECTURE.md §6.4).
/// </summary>
public class DocumentVersion
{
    public Guid Id { get; private set; }
    public Guid StepId { get; private set; }
    public int VersionNumber { get; private set; }
    public string FileName { get; private set; } = default!;
    public string BlobUri { get; private set; } = default!;
    public string ContentType { get; private set; } = default!;
    public long SizeBytes { get; private set; }
    public string Checksum { get; private set; } = default!;
    public Guid UploadedBy { get; private set; }
    public DateTime UploadedAtUtc { get; private set; }

    private DocumentVersion() { } // EF Core

    internal DocumentVersion(
        Guid stepId,
        int versionNumber,
        string fileName,
        string blobUri,
        string contentType,
        long sizeBytes,
        string checksum,
        Guid uploadedBy)
    {
        Id = Guid.NewGuid();
        StepId = stepId;
        VersionNumber = versionNumber;
        FileName = fileName;
        BlobUri = blobUri;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Checksum = checksum;
        UploadedBy = uploadedBy;
        UploadedAtUtc = DateTime.UtcNow;
    }
}
