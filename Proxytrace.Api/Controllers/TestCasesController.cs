using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Proxytrace.Api.Auth;
using Proxytrace.Api.Dto.TestSuites;
using Proxytrace.Domain;
using Proxytrace.Domain.AuditLog;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.TestCase;
using Proxytrace.Domain.TestSuite;

namespace Proxytrace.Api.Controllers;

/// <summary>
/// API controller for test cases operations.
/// </summary>
[ApiController]
[Authorize]
[Route("api/test-cases")]
public class TestCasesController : ControllerBase
{
    private readonly IRepository<ITestCase> repository;
    private readonly ITestSuiteRepository suiteRepository;
    private readonly ITestCase.CreateExisting createExisting;
    private readonly TestSuiteDtoMapper mapper;
    private readonly IProjectAccessGuard accessGuard;
    private readonly ILogger<Audit> audit;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestCasesController"/> class.
    /// </summary>
    public TestCasesController(
        IRepository<ITestCase> repository,
        ITestSuiteRepository suiteRepository,
        ITestCase.CreateExisting createExisting,
        TestSuiteDtoMapper mapper,
        IProjectAccessGuard accessGuard,
        ILogger<Audit> audit)
    {
        this.repository = repository;
        this.suiteRepository = suiteRepository;
        this.createExisting = createExisting;
        this.mapper = mapper;
        this.accessGuard = accessGuard;
        this.audit = audit;
    }

    // A test case has no direct project navigation — it is referenced by a suite (suite.TestCases),
    // and a suite belongs to a project via suite.Agent.Project. Resolve access by checking whether
    // any suite in a project the caller can access contains this test case. Admins bypass.
    private async Task<bool> CanAccessTestCaseAsync(Guid testCaseId, CancellationToken cancellationToken)
    {
        var accessible = await accessGuard.GetAccessibleProjectIdsAsync(cancellationToken);
        if (accessible is null)
            return true;
        foreach (var projectId in accessible)
        {
            var suites = await suiteRepository.GetByProjectAsync(projectId, cancellationToken);
            if (suites.Any(s => s.TestCases.Any(tc => tc.Id == testCaseId)))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Gets.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TestCaseDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var tc = await repository.FindAsync(id, cancellationToken);
        if (tc is null) return NotFound();
        if (!await CanAccessTestCaseAsync(id, cancellationToken)) return NotFound();
        return ToDto(tc);
    }

    /// <summary>
    /// Updates.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TestCaseDto>> Update(
        Guid id,
        [FromBody] UpdateTestCaseRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await repository.FindAsync(id, cancellationToken);
        if (existing is null) return NotFound();
        if (!await CanAccessTestCaseAsync(id, cancellationToken)) return NotFound();

        var expected = mapper.BuildAssistantMessage(request.ExpectedOutput);
        // Editing the expected answer must not sever the provenance link — carry the source trace id
        // through the reconstitution.
        var updated = createExisting(existing.Input, expected, existing.SourceAgentCallId, existing);
        var saved = await repository.UpdateAsync(updated, cancellationToken);

        // A test case has no FK to a project; resolve the owning project via the suite->agent reverse
        // projection so the entry is attributed to the project (not a global/admin-only row). Emit only
        // after the update persists.
        var projectId = await suiteRepository.GetProjectIdByTestCaseAsync(id, cancellationToken);
        audit.LogAudit(AuditAction.TestCaseUpdated, nameof(ITestCase), saved.Id, projectId: projectId);
        return ToDto(saved);
    }

    private TestCaseDto ToDto(ITestCase tc) => new(
        tc.Id,
        tc.Input.Messages.Select(m => new TestSuiteMessageDto(m.Role.ToString().ToLower(), GetText(m))).ToArray(),
        mapper.ToExpectedOutputDto(tc.ExpectedOutput),
        tc.SourceAgentCallId);

    private static string GetText(Message m) => m switch
    {
        UserMessage u => string.Concat(u.Contents.Select(c => c.Text ?? "")),
        AssistantMessage a => string.Concat(a.Contents.Select(c => c.Text ?? "")),
        SystemMessage s => string.Concat(s.Contents.Select(c => c.Text ?? "")),
        ToolMessage t => t.Contents.Count > 1 ? t.Contents[1].Text ?? "" : "",
        _ => ""
    };
}
