using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WorldEventAlerts.Api.Features.Accounts.Domain;
using WorldEventAlerts.Api.Infrastructure.Persistence;

namespace WorldEventAlerts.Api.Infrastructure.Accounts;

// Hosted-service startup completes before the server accepts requests. There is no HTTP provisioning path.
internal sealed class DevelopmentAdminBootstrap(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<DevelopmentAdminBootstrap> logger) : IHostedService
{
    private const string FailureMessage = "Development administrator bootstrap failed. Check local configuration, account-name availability, and explicitly applied migrations.";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Do not even read credentials or resolve persistence outside Development.
        if (!environment.IsDevelopment()) return;

        try
        {
            var enabledValue = configuration["DevelopmentAdmin:Enabled"];
            if (enabledValue is null) return;
            if (!bool.TryParse(enabledValue, out var enabled)) throw new InvalidOperationException();
            if (!enabled) return;
            cancellationToken.ThrowIfCancellationRequested();

            var configuredName = configuration["DevelopmentAdmin:UserName"];
            var password = configuration["DevelopmentAdmin:Password"];
            if (!UserName.TryCreate(configuredName, out var name) || string.IsNullOrWhiteSpace(password) || password.Length > 128)
                throw new InvalidOperationException();

            await using var scope = scopes.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<AlertsDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            // Identity's stores share this scoped context. Their separate SaveChanges calls must
            // commit as one unit, including role creation. No auto-migration or schema creation.
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var existing = await users.FindByNameAsync(name!.Value);
            cancellationToken.ThrowIfCancellationRequested();
            if (existing is not null)
            {
                // Normalized-name collisions are never an instruction to promote/reset an account.
                if (!await users.IsInRoleAsync(existing, AccountRoles.Administrator)) throw new InvalidOperationException();
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }

            var user = new IdentityUser(name.Value);
            EnsureSucceeded(await users.CreateAsync(user, password));
            cancellationToken.ThrowIfCancellationRequested();
            if (!await roles.RoleExistsAsync(AccountRoles.Administrator))
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureSucceeded(await roles.CreateAsync(new IdentityRole(AccountRoles.Administrator)));
            }
            cancellationToken.ThrowIfCancellationRequested();
            EnsureSucceeded(await users.AddToRoleAsync(user, AccountRoles.Administrator));
            cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Development administrator bootstrap created an account.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception)
        {
            // Host startup logs exceptions, unlike the HTTP exception handler. Never let raw
            // Identity/provider/configuration diagnostics or an inner exception reach that log.
            throw new InvalidOperationException(FailureMessage);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException();
    }
}
