using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nordstein.Core.Common.Async;
using Proxytrace.Domain.Model;

namespace Proxytrace.Storage.Internal.Entities.Model;

internal class ModelConfig : AbstractEntityConfiguration<ModelEntity>, IMapper<IModel, ModelEntity>
{
    private readonly IModel.CreateExisting factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelConfig"/> class.
    /// </summary>
    public ModelConfig(IModel.CreateExisting factory)
    {
        this.factory = factory;
    }

    /// <summary>
    /// Configures the application request pipeline.
    /// </summary>
    public override void Configure(EntityTypeBuilder<ModelEntity> builder)
    {
        builder.HasIndex(e => e.Name).IsUnique();
        builder.Property(e => e.Name).HasMaxLength(256).IsRequired();
    }

    /// <summary>
    /// Maps.
    /// </summary>
    public Task<IModel> Map(ModelEntity stored, CancellationToken cancellationToken = default)
        => factory(stored.Name, stored).ToTaskResult();

    /// <summary>
    /// Maps.
    /// </summary>
    public Task<ModelEntity> Map(IModel domain, CancellationToken cancellationToken = default)
        => new ModelEntity
        {
            Id = domain.Id,
            Name = domain.Name,
            CreatedAt = domain.CreatedAt,
            UpdatedAt = domain.UpdatedAt,
        }.ToTaskResult();
}

