using Microsoft.EntityFrameworkCore;

namespace WorldEventAlerts.Api.Infrastructure.Persistence;

// No placeholder entities or schema. Business models arrive in their approved increments.
public sealed class AlertsDbContext(DbContextOptions<AlertsDbContext> options) : DbContext(options);
