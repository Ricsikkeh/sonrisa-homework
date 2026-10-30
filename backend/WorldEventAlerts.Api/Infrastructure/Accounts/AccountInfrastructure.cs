using Microsoft.AspNetCore.Identity;
using WorldEventAlerts.Api.Features.Accounts.Application;
using WorldEventAlerts.Api.Infrastructure.Persistence;

namespace WorldEventAlerts.Api.Infrastructure.Accounts;

internal static class AccountInfrastructure
{
    public static IServiceCollection AddAccountInfrastructure(this IServiceCollection services, IHostEnvironment environment)
    {
        services.AddIdentity<IdentityUser, IdentityRole>()
            .AddEntityFrameworkStores<AlertsDbContext>();
        services.AddScoped<IAccountIdentity, IdentityAccountGateway>();
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "wea.session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = false;
            options.Events.OnValidatePrincipal = async context =>
            {
                await SecurityStampValidator.ValidatePrincipalAsync(context);
                // Stamp validation must not extend the absolute session lifetime.
                context.ShouldRenew = false;
            };
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-XSRF-TOKEN";
            options.Cookie.Name = "wea.antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
        services.AddAuthorization();
        return services;
    }
}
