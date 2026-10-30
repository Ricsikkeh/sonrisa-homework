namespace WorldEventAlerts.Api.Features.Accounts.Application;

internal static class AccountApplication
{
    public static IServiceCollection AddAccountApplication(this IServiceCollection services)
    {
        services.AddScoped<IAccountService, AccountService>();
        return services;
    }
}
