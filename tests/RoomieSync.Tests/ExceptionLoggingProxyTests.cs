using RoomieSync.Core.Common;

namespace RoomieSync.Tests;

public sealed class ExceptionLoggingProxyTests
{
    [Fact]
    public async Task LogsAndRethrowsSyncAndAsyncFailures()
    {
        var syncException = new InvalidOperationException("sync failure");
        var asyncException = new InvalidOperationException("async failure");
        var proxy = ExceptionLoggingProxy<IFailingOperations>.Create(new FailingOperations(syncException, asyncException));

        var thrownSyncException = Assert.Throws<InvalidOperationException>(() => proxy.ThrowSynchronously());
        var thrownAsyncException = await Assert.ThrowsAsync<InvalidOperationException>(() => proxy.ThrowAsynchronously());

        Assert.Same(syncException, thrownSyncException);
        Assert.Same(asyncException, thrownAsyncException);
    }

    public interface IFailingOperations
    {
        void ThrowSynchronously();
        Task ThrowAsynchronously();
    }

    private sealed class FailingOperations(Exception syncException, Exception asyncException) : IFailingOperations
    {
        public void ThrowSynchronously() => throw syncException;

        public Task ThrowAsynchronously() => Task.FromException(asyncException);
    }
}
