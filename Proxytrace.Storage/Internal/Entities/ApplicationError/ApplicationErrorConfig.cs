using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nordstein.Core.Common.Async;
using Proxytrace.Domain.ApplicationError;

namespace Proxytrace.Storage.Internal.Entities.ApplicationError;

internal class ApplicationErrorConfig
    : AbstractEntityConfiguration<ApplicationErrorEntity>,
      IMapper<IApplicationError, ApplicationErrorEntity>
{
    private readonly IApplicationError.CreateExisting factory;

    public ApplicationErrorConfig(IApplicationError.CreateExisting factory)
    {
        this.factory = factory;
    }

    public override void Configure(EntityTypeBuilder<ApplicationErrorEntity> builder)
    {
        builder.HasIndex(e => e.CreatedAt);
        builder.Property(e => e.Message).HasMaxLength(4096);
        builder.Property(e => e.Category).HasMaxLength(512);
        builder.Property(e => e.ExceptionType).HasMaxLength(512);
        // StackTrace is intentionally unbounded (Postgres text) — stacktraces are large.
    }

    public Task<IApplicationError> Map(ApplicationErrorEntity stored, CancellationToken cancellationToken = default)
        => factory(
            stored.Message,
            stored.Level,
            stored.Category,
            stored.ExceptionType,
            stored.StackTrace,
            stored).ToTaskResult();

    public Task<ApplicationErrorEntity> Map(IApplicationError domain, CancellationToken cancellationToken = default)
        => new ApplicationErrorEntity
        {
            Id = domain.Id,
            Message = domain.Message,
            Level = domain.Level,
            Category = domain.Category,
            ExceptionType = domain.ExceptionType,
            StackTrace = domain.StackTrace,
            CreatedAt = domain.CreatedAt,
            UpdatedAt = domain.UpdatedAt,
        }.ToTaskResult();
}
