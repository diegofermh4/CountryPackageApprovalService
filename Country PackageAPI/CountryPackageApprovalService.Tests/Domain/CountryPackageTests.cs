using CountryPackageApprovalService.Domain;

namespace CountryPackageApprovalService.Tests.Domain;

public class CountryPackageTests
{
    [Fact]
    public void CreateFromTemplate_produces_the_four_steps_from_the_brief_in_order()
    {
        var package = CountryPackage.CreateFromTemplate("RUR", RoadmapTemplate.CreateDefault(), "Test", Guid.NewGuid());

        Assert.Equal(4, package.Steps.Count);
        Assert.Equal(new[] { 1, 2, 3, 4 }, package.Steps.Select(s => s.StepOrder));
        Assert.Equal(1, package.CurrentStepOrder);
        Assert.Equal("InProgress", package.Status);
    }

    [Fact]
    public void AdvanceIfCurrentStepCompleted_never_advances_past_the_last_step()
    {
        var package = CountryPackage.CreateFromTemplate("RUR", RoadmapTemplate.CreateDefault(), "Test", Guid.NewGuid());

        for (var order = 1; order <= 4; order++)
        {
            var step = package.GetStep(order);
            if (step.RequiresDocument)
                step.AttachDocument(Guid.NewGuid(), "doc.pdf", "file://doc", "application/pdf", 10, "chk");

            var approver = Guid.NewGuid();
            step.Submit(Guid.NewGuid(), approver);
            if (step.StepType == StepType.Decision)
                step.Approve(approver, "ok");

            package.AdvanceIfCurrentStepCompleted();
        }

        Assert.Equal(4, package.CurrentStepOrder);
        Assert.Equal("Completed", package.Status);

        // Calling it again at the tail must stay a no-op, not throw or move past Steps.Count.
        package.AdvanceIfCurrentStepCompleted();
        Assert.Equal(4, package.CurrentStepOrder);
    }

    [Fact]
    public void Status_reports_ReturnedForRevision_when_the_current_step_was_returned()
    {
        var package = CountryPackage.CreateFromTemplate("RUR", RoadmapTemplate.CreateDefault(), "Test", Guid.NewGuid());
        var step = package.GetStep(1);
        step.AttachDocument(Guid.NewGuid(), "doc.pdf", "file://doc", "application/pdf", 10, "chk");
        var approver = Guid.NewGuid();
        step.Submit(Guid.NewGuid(), approver);
        step.Return(approver, "revise please");

        Assert.Equal("ReturnedForRevision", package.Status);
    }

    [Fact]
    public void CreateFromTemplate_from_an_inactive_template_throws()
    {
        var template = new RoadmapTemplate(Guid.NewGuid(), "Retired", version: 0, isActive: false);
        template.AddStep(1, StepType.Decision, OrgLevel.Country, "Retired step");

        Assert.Throws<CountryPackageApprovalService.Domain.Exceptions.BusinessRuleValidationException>(() =>
            CountryPackage.CreateFromTemplate("RUR", template, "Test", Guid.NewGuid()));
    }
}
