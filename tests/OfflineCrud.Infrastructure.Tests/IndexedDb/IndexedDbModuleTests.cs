using Microsoft.JSInterop;
using OfflineCrud.Infrastructure.IndexedDb;

namespace OfflineCrud.Infrastructure.Tests.IndexedDb;

[TestFixture]
public class IndexedDbModuleTests
{
    [Test]
    public async Task FailedImport_IsRetriedOnTheNextCall()
    {
        // e.g. the module 404s once right after a rebuild, then is served normally.
        FlakyImportRuntime runtime = new(failuresBeforeSuccess: 1);
        await using IndexedDbModule module = new(runtime);

        Assert.That(() => module.InvokeVoidAsync("getMeta", CancellationToken.None), Throws.InstanceOf<JSException>());
        await module.InvokeVoidAsync("getMeta", CancellationToken.None);

        Assert.That(runtime.ImportAttempts, Is.EqualTo(2));
    }

    [Test]
    public async Task SuccessfulImport_IsReused()
    {
        FlakyImportRuntime runtime = new(failuresBeforeSuccess: 0);
        await using IndexedDbModule module = new(runtime);

        await module.InvokeVoidAsync("getMeta", CancellationToken.None);
        await module.InvokeVoidAsync("getMeta", CancellationToken.None);

        Assert.That(runtime.ImportAttempts, Is.EqualTo(1));
    }

    private sealed class FlakyImportRuntime(int failuresBeforeSuccess) : IJSRuntime
    {
        public int ImportAttempts { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Assert.That(identifier, Is.EqualTo("import"));
            ImportAttempts++;
            if (ImportAttempts <= failuresBeforeSuccess)
            {
                return ValueTask.FromException<TValue>(new JSException("Failed to fetch dynamically imported module"));
            }

            return ValueTask.FromResult((TValue)(object)new NoOpModule());
        }
    }

    private sealed class NoOpModule : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            ValueTask.FromResult(default(TValue)!);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
