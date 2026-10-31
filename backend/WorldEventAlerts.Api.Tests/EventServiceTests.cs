using Microsoft.Extensions.DependencyInjection;
using WorldEventAlerts.Api.Features.Events.Application;
using WorldEventAlerts.Api.Features.Events.Domain;

namespace WorldEventAlerts.Api.Tests;

public sealed class EventServiceTests
{
    [Fact]
    public async Task Scoped_service_forwards_exact_arguments_and_typed_outcome()
    {
        var store = new Store();
        var services = new ServiceCollection();
        services.AddSingleton<IEventStore>(store);
        services.AddEventApplication();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IEventService>();
        using var cancellation = new CancellationTokenSource();
        var ownerId = "owner-123";
        var key = Guid.NewGuid();
        Assert.True(EventContent.TryCreate("Title", "Message", out var content));
        var request = new CreateEvent(Guid.NewGuid(), content!);
        var outcome = new EventResult.TypeUnavailable();
        store.Result = outcome;

        var actual = await service.CreateAsync(ownerId, key, request, cancellation.Token);

        Assert.Same(outcome, actual);
        Assert.Equal(ownerId, store.OwnerId);
        Assert.Equal(key, store.Key);
        Assert.Same(request, store.Request);
        Assert.Equal(cancellation.Token, store.Token);
        Assert.Equal(1, store.Calls);
    }

    [Fact]
    public async Task Pre_cancelled_call_does_not_call_store()
    {
        var store = new Store();
        var services = new ServiceCollection();
        services.AddSingleton<IEventStore>(store);
        services.AddEventApplication();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IEventService>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.True(EventContent.TryCreate("Title", "Message", out var content));
        var request = new CreateEvent(Guid.NewGuid(), content!);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.CreateAsync("owner", Guid.NewGuid(), request, cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public void Scoped_registration_creates_separate_service_instances_per_scope()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEventStore>(new Store());
        services.AddEventApplication();
        using var provider = services.BuildServiceProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var first = firstScope.ServiceProvider.GetRequiredService<IEventService>();
        var second = secondScope.ServiceProvider.GetRequiredService<IEventService>();

        Assert.NotSame(first, second);
        Assert.Same(first, firstScope.ServiceProvider.GetRequiredService<IEventService>());
    }

    private sealed class Store : IEventStore
    {
        public int Calls { get; private set; }
        public string? OwnerId { get; private set; }
        public Guid Key { get; private set; }
        public CreateEvent? Request { get; private set; }
        public CancellationToken Token { get; private set; }
        public EventResult Result { get; set; } = new EventResult.InProgress();

        public Task<EventResult> CreateAsync(string ownerId, Guid key, CreateEvent request, CancellationToken cancellationToken)
        {
            Calls++;
            OwnerId = ownerId;
            Key = key;
            Request = request;
            Token = cancellationToken;
            return Task.FromResult(Result);
        }
    }
}
