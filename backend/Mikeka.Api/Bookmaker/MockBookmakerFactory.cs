using Mikeka.Api.Domain;

namespace Mikeka.Api.Bookmaker;

public class MockBookmakerFactory(TimeProvider clock) : IBookmakerFactory
{
    public Task<IBookmakerClient> CreateAsync(Account account, string password, CancellationToken ct)
        => Task.FromResult<IBookmakerClient>(new MockBookmakerClient(clock));
}
