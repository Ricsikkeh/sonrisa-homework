using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WorldEventAlerts.Api.Features.EventTypes.Domain;
using WorldEventAlerts.Api.Infrastructure.EventTypes;

namespace WorldEventAlerts.Api.Infrastructure.Persistence;

public sealed class AlertsDbContext(DbContextOptions<AlertsDbContext> options) : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<EventType> EventTypes => Set<EventType>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfiguration(new EventTypeConfiguration());
    }
}
