using System.Security.Cryptography;
using CountryPackageApprovalService.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CountryPackageApprovalService.Infrastructure.DocumentStore;

/// <summary>
/// Local-disk document store for this exercise; swaps for Azure Blob Storage (versioned containers behind an
/// immutability policy) in the Azure target architecture without any Application-layer change
/// (docs/ARCHITECTURE.md §6.4) - callers only ever depend on <see cref="IDocumentStore"/>. Every upload gets
/// its own GUID-prefixed file name (never overwritten) and a SHA-256 checksum computed while streaming to
/// disk, so <see cref="Domain.DocumentVersion"/> rows can carry an integrity check independent of the
/// underlying storage.
/// </summary>
public sealed class LocalDiskDocumentStore : IDocumentStore
{
    private readonly string _basePath;
    private readonly ILogger<LocalDiskDocumentStore> _logger;

    public LocalDiskDocumentStore(IConfiguration configuration, ILogger<LocalDiskDocumentStore> logger)
    {
        _basePath = configuration["DocumentStorage:BasePath"] ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "documents");
        Directory.CreateDirectory(_basePath);
        _logger = logger;
    }

    public async Task<DocumentStoreResult> SaveAsync(
        Guid packageId, Guid stepId, Stream content, string fileName, string contentType, CancellationToken ct)
    {
        var stepDir = Path.Combine(_basePath, packageId.ToString(), stepId.ToString());
        Directory.CreateDirectory(stepDir);

        var safeFileName = $"{Guid.NewGuid():N}_{Path.GetFileName(fileName)}";
        var fullPath = Path.Combine(stepDir, safeFileName);

        using var sha256 = SHA256.Create();
        await using var fileStream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await using (var hashingStream = new CryptoStream(fileStream, sha256, CryptoStreamMode.Write, leaveOpen: true))
        {
            await content.CopyToAsync(hashingStream, ct);
            await hashingStream.FlushFinalBlockAsync(ct);
        }

        var checksum = Convert.ToHexString(sha256.Hash ?? Array.Empty<byte>());
        var sizeBytes = fileStream.Length;

        _logger.LogInformation(
            "Stored document {FileName} for step {StepId} of package {PackageId} ({SizeBytes} bytes, sha256:{Checksum})",
            fileName, stepId, packageId, sizeBytes, checksum);

        // file:// URI in this exercise; https://<account>.blob.core.windows.net/... in the Azure target architecture.
        return new DocumentStoreResult(new Uri(fullPath).AbsoluteUri, sizeBytes, checksum);
    }
}
