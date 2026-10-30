using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldEventAlerts.Api.Features.EventTypes.Domain;

namespace WorldEventAlerts.Api.Infrastructure.EventTypes;

internal sealed class EventTypeConfiguration : IEntityTypeConfiguration<EventType>
{
    public void Configure(EntityTypeBuilder<EventType> entity)
    {
        entity.ToTable("EventTypes");
        entity.HasKey(type => type.Id);
        entity.Property(type => type.Id).ValueGeneratedNever();
        entity.Property(type => type.Name).HasMaxLength(EventTypeName.MaxLength).IsRequired();
        entity.Property(type => type.NormalizedName).HasMaxLength(EventTypeName.MaxLength).IsRequired();
        entity.HasIndex(type => type.NormalizedName).IsUnique().HasFilter("\"DeletedAt\" IS NULL");
        entity.HasQueryFilter(type => type.DeletedAt == null);
    }
}
