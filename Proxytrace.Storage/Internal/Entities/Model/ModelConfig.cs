using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nordstein.Core.Common.Async;
using Proxytrace.Domain.Model;

namespace Proxytrace.Storage.Internal.Entities.Model;

internal class ModelConfig : AbstractEntityConfiguration<ModelEntity>, IMapper<IModel, ModelEntity>
{
    private readonly IModel.CreateExisting factory;

    public ModelConfig(IModel.CreateExisting factory)
    {
        this.factory = factory;
    }

    public override void Configure(EntityTypeBuilder<ModelEntity> builder)
    {
        builder.HasIndex(e => e.Name).IsUnique();
        builder.Property(e => e.Name).HasMaxLength(256).IsRequired();
    }

    public Task<IModel> Map(ModelEntity stored, CancellationToken cancellationToken = default)
        => factory(stored.Name, stored).ToTaskResult();

    public Task<ModelEntity> Map(IModel domain, CancellationToken cancellationToken = default)
        => new ModelEntity
        {
            Id = domain.Id,
            Name = domain.Name,
            CreatedAt = domain.CreatedAt,
            UpdatedAt = domain.UpdatedAt,
        }.ToTaskResult();
}

