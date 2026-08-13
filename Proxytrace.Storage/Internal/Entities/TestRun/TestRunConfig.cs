using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nordstein.Core.Common.Async;
using Nordstein.Core.Common.Serialization;
using Proxytrace.Domain;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.TestResult;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;
using Proxytrace.Storage.Internal.Entities.ModelEndpoint;
using Proxytrace.Storage.Internal.Entities.TestRunGroup;

namespace Proxytrace.Storage.Internal.Entities.TestRun;

internal class TestRunConfig : AbstractEntityConfiguration<TestRunEntity>, IMapper<ITestRun, TestRunEntity>
{
    private readonly IRepository<IModelEndpoint> endpoints;
    private readonly IRepository<ITestResult> testResults;
    private readonly IRepository<ITestRunGroup> groups;
    private readonly ITestRun.CreateExisting factory;
    private readonly ISerializer serializer;

    public TestRunConfig(
        IRepository<IModelEndpoint> endpoints,
        IRepository<ITestResult> testResults,
        IRepository<ITestRunGroup> groups,
        ITestRun.CreateExisting factory,
        ISerializer serializer)
    {
        this.endpoints = endpoints;
        this.testResults = testResults;
        this.groups = groups;
        this.factory = factory;
        this.serializer = serializer;
    }

    public override void Configure(EntityTypeBuilder<TestRunEntity> builder)
    {
        builder
            .HasOne<ModelEndpointEntity>()
            .WithMany()
            .HasForeignKey(e => e.Endpoint)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne<TestRunGroupEntity>()
            .WithMany()
            .HasForeignKey(e => e.Group)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .Property(e => e.TestResults)
            .HasConversion(
                v => serializer.Serialize(v),
                v => serializer.Deserialize<IReadOnlyCollection<Guid>>(v) ?? Array.Empty<Guid>()
            );
    }

    public async Task<ITestRun> Map(TestRunEntity stored, CancellationToken cancellationToken = default)
    {
        // Sequential, not concurrent: these reads may share the ambient transaction context,
        // and a DbContext does not allow concurrent operations.
        var group = await groups.GetAsync(stored.Group, cancellationToken);
        var endpoint = await endpoints.GetAsync(stored.Endpoint, cancellationToken);
        var results = await testResults.GetManyAsync(stored.TestResults, cancellationToken, ignoreMissing: true);

        return factory(
            group: group,
            endpoint: endpoint,
            sampleIndex: stored.SampleIndex,
            status: stored.Status,
            completedAt: stored.CompletedAt,
            testResults: results,
            existing: stored);
    }

    public Task<TestRunEntity> Map(ITestRun domain, CancellationToken cancellationToken = default)
        => new TestRunEntity
        {
            Id = domain.Id,
            Group = domain.Group.Id,
            Endpoint = domain.Endpoint.Id,
            SampleIndex = domain.SampleIndex,
            Status = domain.Status,
            CompletedAt = domain.CompletedAt,
            TestResults = domain.TestResults.Select(x => x.Id).ToArray(),
            CreatedAt = domain.CreatedAt,
            UpdatedAt = domain.UpdatedAt,
        }.ToTaskResult();
}
