using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace WorldEventAlerts.Api.Infrastructure.Persistence;

public sealed class AlertsDbContext(DbContextOptions<AlertsDbContext> options) : IdentityDbContext<IdentityUser>(options);
