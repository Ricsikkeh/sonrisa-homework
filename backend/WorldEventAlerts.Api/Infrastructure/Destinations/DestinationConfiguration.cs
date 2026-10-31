using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldEventAlerts.Api.Features.Destinations.Domain;

namespace WorldEventAlerts.Api.Infrastructure.Destinations;

internal sealed class DestinationConfiguration : IEntityTypeConfiguration<UserDestinations>
{
    public void Configure(EntityTypeBuilder<UserDestinations> builder)
    {
        builder.ToTable("UserDestinations");
        builder.HasKey(item => item.OwnerId);
        builder.Property(item => item.Email).HasMaxLength(DestinationSettings.EmailMaxLength);
        builder.Property(item => item.SlackLabel).HasMaxLength(DestinationSettings.SlackLabelMaxLength);
        builder.HasOne<IdentityUser>().WithOne().HasForeignKey<UserDestinations>(item => item.OwnerId).OnDelete(DeleteBehavior.Cascade);
    }
}
