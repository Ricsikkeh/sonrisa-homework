using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldEventAlerts.Api.Features.EventTypes.Domain;
using WorldEventAlerts.Api.Features.Subscriptions.Domain;

namespace WorldEventAlerts.Api.Infrastructure.Subscriptions;

internal sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("Subscriptions", table => table.HasCheckConstraint("CK_Subscriptions_Channels", "\"Email\" OR \"Slack\""));
        builder.HasKey(item => item.Id);
        builder.HasIndex(item => new { item.OwnerId, item.EventTypeId }).IsUnique();
        builder.HasOne<IdentityUser>().WithMany().HasForeignKey(item => item.OwnerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EventType>().WithMany().HasForeignKey(item => item.EventTypeId).OnDelete(DeleteBehavior.Restrict);
    }
}
