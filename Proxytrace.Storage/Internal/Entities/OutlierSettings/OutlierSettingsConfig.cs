using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Proxytrace.Storage.Internal.Entities.OutlierSettings;

internal class OutlierSettingsConfig : AbstractEntityConfiguration<OutlierSettingsEntity>
{
    /// <summary>
    /// Configures the application request pipeline.
    /// </summary>
    public override void Configure(EntityTypeBuilder<OutlierSettingsEntity> builder)
    {
    }
}
