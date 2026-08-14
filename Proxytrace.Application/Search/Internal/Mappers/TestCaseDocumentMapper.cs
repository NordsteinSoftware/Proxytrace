using System.Runtime.CompilerServices;
using System.Text;
using Lucene.Net.Documents;
using Microsoft.Extensions.Logging;
using Proxytrace.Domain;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.Search;
using Proxytrace.Domain.TestCase;
using Proxytrace.Domain.TestSuite;

namespace Proxytrace.Application.Search.Internal.Mappers;

/// <summary>
/// Indexes test cases for search. TestCase has no direct project reference, so we resolve the
/// owning project via the parent test suite. Live indexing requires ITestCase : ISearchable;
/// for now, indexing happens on full-project reindex.
/// </summary>
internal sealed class TestCaseDocumentMapper : IDocumentMapper
{
    private readonly IRepository<ITestCase> testCases;
    private readonly ITestSuiteRepository testSuites;
    private readonly ILogger<TestCaseDocumentMapper> logger;

    public SearchKind Kind => SearchKind.TestCase;

    public Type EntityType => typeof(ITestCase);

    public TestCaseDocumentMapper(
        IRepository<ITestCase> testCases,
        ITestSuiteRepository testSuites,
        ILogger<TestCaseDocumentMapper> logger)
    {
        this.testCases = testCases;
        this.testSuites = testSuites;
        this.logger = logger;
    }

    public async Task<Document?> BuildAsync(Guid entityId, CancellationToken cancellationToken)
    {
        var tc = await testCases.FindAsync(entityId, cancellationToken);
        if (tc is null) return null;

        // Locate parent suite to get project. TestCase has no SuiteId FK — it lives inside the
        // suite's serialized TestCases column — so the owning suite can't be found with a SQL
        // WHERE and this scans all suites. Acceptable only because this single-entity path is a
        // fallback; bulk reindex uses the project-scoped BuildAllForProjectAsync below. A real
        // fix needs a TestCase→Suite FK (schema change) before this can become a filtered query.
        var allSuites = await testSuites.GetAllAsync(cancellationToken);
        var suite = allSuites.FirstOrDefault(s => s.TestCases.Any(c => c.Id == entityId));
        return suite is null 
            ? null 
            : BuildDocument(tc, suite);
    }

    public async IAsyncEnumerable<Document> BuildAllForProjectAsync(Guid projectId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var suites = await testSuites.GetByProjectAsync(projectId, cancellationToken);
        var seen = new HashSet<Guid>();
        foreach (var suite in suites)
        {
            foreach (var tc in suite.TestCases)
            {
                if (!seen.Add(tc.Id))
                    continue;
                
                Document? document = null;
                try
                {
                    document = BuildDocument(tc, suite);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to build document for TestCase {TestCaseId} in suite {SuiteId}", tc.Id, suite.Id);
                }

                if (document != null)
                {
                    yield return document;    
                }
            }
        }
    }

    private static Document BuildDocument(ITestCase tc, ITestSuite suite)
    {
        var body = new StringBuilder();
        foreach (var msg in tc.Input.Messages)
        {
            foreach (var content in msg.Contents)
            {
                if (!string.IsNullOrEmpty(content.Text)) body.Append(content.Text).Append('\n');
            }
        }
        foreach (var content in tc.ExpectedOutput.Contents)
        {
            if (!string.IsNullOrEmpty(content.Text)) body.Append(content.Text).Append('\n');
        }

        var firstUser = tc.Input.Messages
            .OfType<UserMessage>()
            .SelectMany(m => m.Contents)
            .Select(c => c.Text)
            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? "";
        var preview = firstUser.Length > 60 ? firstUser[..60] + "…" : firstUser;
        var title = $"Test case · {suite.Name} · {preview}";

        return DocumentBuilder.Build(
            kind: SearchKind.TestCase,
            entityId: tc.Id,
            projectId: suite.Agent.Project.Id,
            createdAt: tc.CreatedAt,
            title: title,
            body: body.ToString(),
            boostedBody: suite.Name);
    }
}
