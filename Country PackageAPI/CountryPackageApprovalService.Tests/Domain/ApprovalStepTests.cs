using CountryPackageApprovalService.Domain;
using CountryPackageApprovalService.Domain.Exceptions;

namespace CountryPackageApprovalService.Tests.Domain;

/// <summary>
/// Pure Domain-level tests: no database, no HTTP host, just <see cref="ApprovalStep"/>'s own state machine
/// (docs/ARCHITECTURE.md §2.3). Fast and deterministic - these are the tests that should catch a broken
/// business rule before anything about persistence or the API even enters the picture.
/// </summary>
public class ApprovalStepTests
{
    private static CountryPackage CreatePackage() =>
        CountryPackage.CreateFromTemplate("RUR", RoadmapTemplate.CreateDefault(), "Test Package", Guid.NewGuid());

    [Fact]
    public void Decision_step_cannot_be_submitted_without_a_document()
    {
        var step = CreatePackage().GetStep(1); // Decision step

        Assert.Throws<InvalidStepStateException>(() => step.Submit(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public void Attach_document_then_submit_moves_step_to_PendingApproval()
    {
        var step = CreatePackage().GetStep(1);
        step.AttachDocument(Guid.NewGuid(), "roadmap.pdf", "file://doc1", "application/pdf", 1024, "checksum1");

        var approverId = Guid.NewGuid();
        step.Submit(Guid.NewGuid(), approverId);

        Assert.Equal(StepStatus.PendingApproval, step.Status);
        Assert.Equal(approverId, step.AssignedApproverId);
    }

    [Fact]
    public void Approve_by_someone_other_than_the_named_approver_throws()
    {
        var step = CreatePackage().GetStep(1);
        step.AttachDocument(Guid.NewGuid(), "roadmap.pdf", "file://doc1", "application/pdf", 1024, "checksum1");
        step.Submit(Guid.NewGuid(), Guid.NewGuid()); // approver A

        Assert.Throws<UnauthorizedStepActionException>(() => step.Approve(Guid.NewGuid(), "not the named approver"));
    }

    [Fact]
    public void Approve_locks_the_step_and_rejects_further_documents()
    {
        var step = CreatePackage().GetStep(1);
        step.AttachDocument(Guid.NewGuid(), "roadmap.pdf", "file://doc1", "application/pdf", 1024, "checksum1");
        var approverId = Guid.NewGuid();
        step.Submit(Guid.NewGuid(), approverId);
        step.Approve(approverId, "looks good");

        Assert.True(step.IsLocked);
        Assert.Equal(StepStatus.Completed, step.Status);
        Assert.Throws<StepLockedException>(() =>
            step.AttachDocument(Guid.NewGuid(), "v2.pdf", "file://doc2", "application/pdf", 2048, "checksum2"));
    }

    [Fact]
    public void Return_without_a_comment_throws()
    {
        var step = CreatePackage().GetStep(1);
        step.AttachDocument(Guid.NewGuid(), "roadmap.pdf", "file://doc1", "application/pdf", 1024, "checksum1");
        var approverId = Guid.NewGuid();
        step.Submit(Guid.NewGuid(), approverId);

        Assert.Throws<InvalidStepStateException>(() => step.Return(approverId, ""));
    }

    [Fact]
    public void Return_then_reattach_preserves_the_previous_document_version()
    {
        var step = CreatePackage().GetStep(1);
        step.AttachDocument(Guid.NewGuid(), "v1.pdf", "file://doc1", "application/pdf", 1024, "checksum1");
        var approverId = Guid.NewGuid();
        step.Submit(Guid.NewGuid(), approverId);
        step.Return(approverId, "Please revise section 3.");

        Assert.Equal(StepStatus.ReturnedForRevision, step.Status);

        var v2 = step.AttachDocument(Guid.NewGuid(), "v2.pdf", "file://doc2", "application/pdf", 2048, "checksum2");

        Assert.Equal(2, v2.VersionNumber);
        Assert.Equal(2, step.Documents.Count);
        Assert.Equal(1, step.Documents[0].VersionNumber); // v1 still there, unchanged
        Assert.Equal("v1.pdf", step.Documents[0].FileName);
    }

    [Fact]
    public void Information_step_completes_immediately_on_submit_and_needs_no_document()
    {
        var step = CreatePackage().GetStep(2); // Information step

        Assert.False(step.RequiresDocument);

        step.Submit(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(StepStatus.Completed, step.Status);
        Assert.True(step.IsLocked);
    }

    [Fact]
    public void Submitting_an_already_pending_step_throws()
    {
        var step = CreatePackage().GetStep(1);
        step.AttachDocument(Guid.NewGuid(), "v1.pdf", "file://doc1", "application/pdf", 1024, "checksum1");
        step.Submit(Guid.NewGuid(), Guid.NewGuid());

        Assert.Throws<InvalidStepStateException>(() => step.Submit(Guid.NewGuid(), Guid.NewGuid()));
    }
}
