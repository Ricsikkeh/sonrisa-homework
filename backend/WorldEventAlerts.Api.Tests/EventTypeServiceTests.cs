using WorldEventAlerts.Api.Features.EventTypes.Application;
using WorldEventAlerts.Api.Features.EventTypes.Domain;

namespace WorldEventAlerts.Api.Tests;

public sealed class EventTypeServiceTests
{
    [Theory]
    [InlineData("list")]
    [InlineData("create")]
    [InlineData("rename")]
    [InlineData("delete")]
    public async Task Application_forwards_validated_values_identifiers_and_original_token(string operation)
    {
        var store = new Store();
        var service = new EventTypeService(store);
        using var cancellation = new CancellationTokenSource();
        var id = Guid.NewGuid();
        Assert.True(EventTypeName.TryCreate(" Weather ", out var name));
        await Execute(service, operation, id, name, cancellation.Token);
        Assert.Equal(1, store.Calls);
        Assert.Equal(cancellation.Token, store.Token);
        if (operation is "create" or "rename") Assert.Same(name, store.Name);
        if (operation is "rename" or "delete") Assert.Equal(id, store.Id);
    }

    [Theory]
    [InlineData("list")]
    [InlineData("create")]
    [InlineData("rename")]
    [InlineData("delete")]
    public async Task Already_cancelled_application_call_does_not_start_persistence(string operation)
    {
        var store = new Store();
        var service = new EventTypeService(store);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.True(EventTypeName.TryCreate("Weather", out var name));
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Execute(service, operation, Guid.NewGuid(), name, cancellation.Token));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(0, store.Calls);
    }

    private static Task Execute(IEventTypeService service, string operation, Guid id, EventTypeName name, CancellationToken token) => operation switch
    {
        "list" => service.ListAsync(token),
        "create" => service.CreateAsync(name, token),
        "rename" => service.RenameAsync(id, name, token),
        _ => service.DeleteAsync(id, token)
    };

    private sealed class Store : IEventTypeStore
    {
        public int Calls { get; private set; }
        public CancellationToken Token { get; private set; }
        public EventTypeName? Name { get; private set; }
        public Guid Id { get; private set; }
        public Task<IReadOnlyList<EventTypeItem>> ListAsync(CancellationToken cancellationToken)
        {
            Calls++; Token = cancellationToken; return Task.FromResult<IReadOnlyList<EventTypeItem>>([]);
        }
        public Task<EventTypeResult> CreateAsync(EventTypeName name, CancellationToken cancellationToken) => Complete(Guid.Empty, name, cancellationToken);
        public Task<EventTypeResult> RenameAsync(Guid id, EventTypeName name, CancellationToken cancellationToken) => Complete(id, name, cancellationToken);
        public Task<EventTypeResult> DeleteAsync(Guid id, CancellationToken cancellationToken) => Complete(id, null, cancellationToken);
        private Task<EventTypeResult> Complete(Guid id, EventTypeName? name, CancellationToken token)
        {
            Calls++; Id = id; Name = name; Token = token; return Task.FromResult<EventTypeResult>(new EventTypeResult.NotFound());
        }
    }
}
