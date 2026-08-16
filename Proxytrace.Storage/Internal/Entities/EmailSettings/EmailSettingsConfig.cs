using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Proxytrace.Storage.Internal.Entities.EmailSettings;

internal class EmailSettingsConfig : AbstractEntityConfiguration<EmailSettingsEntity>
{
    /// <summary>
    /// Configures the application request pipeline.
    /// </summary>
    public override void Configure(EntityTypeBuilder<EmailSettingsEntity> builder)
    {
    }
}
