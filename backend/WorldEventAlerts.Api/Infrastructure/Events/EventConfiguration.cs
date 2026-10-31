using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldEventAlerts.Api.Features.Events.Domain;
using WorldEventAlerts.Api.Features.EventTypes.Domain;

namespace WorldEventAlerts.Api.Infrastructure.Events;

internal sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("Events");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.EventTypeName).HasMaxLength(100).IsRequired();
        builder.Property(item => item.Title).HasMaxLength(EventContent.MaxTitleLength).IsRequired();
        builder.Property(item => item.Message).HasMaxLength(EventContent.MaxMessageLength).IsRequired();
        builder.HasOne<EventType>().WithMany().HasForeignKey(item => item.EventTypeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EventCreationRequestConfiguration : IEntityTypeConfiguration<EventCreationRequest>
{
    public void Configure(EntityTypeBuilder<EventCreationRequest> builder)
    {
        builder.ToTable("EventCreationRequests");
        builder.HasKey(item => new { item.OwnerId, item.Key }).HasName("PK_EventCreationRequests");
        builder.Property(item => item.RequestHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(item => item.EventId).IsUnique();
        builder.HasOne<Event>().WithOne().HasForeignKey<EventCreationRequest>(item => item.EventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IdentityUser>().WithMany().HasForeignKey(item => item.OwnerId).OnDelete(DeleteBehavior.Restrict);
    }
}
