namespace CountryPackageApprovalService.Domain;


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
