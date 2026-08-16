using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Proxytrace.Storage.Internal.Entities.Licensing;

internal class StoredLicenseConfig : AbstractEntityConfiguration<StoredLicenseEntity>
{
    /// <summary>
    /// Configures the application request pipeline.
    /// </summary>
    public override void Configure(EntityTypeBuilder<StoredLicenseEntity> builder)
    {
    }
}
