using Moq;
using OfflineCrud.Application.Common;
using OfflineCrud.Application.Products;
using OfflineCrud.Application.Sync;
using OfflineCrud.Application.Tests.Domain;
using OfflineCrud.Application.Tests.Fakes;
using OfflineCrud.Domain.Products;

namespace OfflineCrud.Application.Tests.Sync;

[TestFixture]
public class SyncServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly ProductDetails V1 = ProductDetailsTests.Create(title: "Version 1");
    private static readonly ProductDetails V2 = ProductDetailsTests.Create(title: "Version 2");

    private InMemoryLocalStore _store = null!;
    private Mock<IProductApiClient> _api = null!;
    private ManualTimeProvider _time = null!;
    private LocalStoreLock _lock = null!;
    private SyncService _sync = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new InMemoryLocalStore();
        _api = new Mock<IProductApiClient>(MockBehavior.Strict);
        _time = new ManualTimeProvider(Now);
        _lock = new LocalStoreLock();
        _sync = new SyncService(_store, _store, _api.Object, _lock, _store, _time, new FixedRandom(0.5));
    }

    [TearDown]
    public void TearDown()
    {
        _sync.Dispose();
        _lock.Dispose();
    }

    [Test]
    public async Task Create_Succeeds_RecordsTheServerIdAndClearsTheEntry()
    {
        (Product product, OutboxEntry create) = SeedLocalCreate(V1);
        _api.Setup(api => api.CreateAsync(V1, create.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServerProduct(195, V1, null, null, null));

        SyncRunResult result = await _sync.RunAsync(CancellationToken.None);

        Product synced = _store.Product(product.Id)!;
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new SyncRunResult(Sent: 1, Retrying: 0, Failed: 0, Waiting: 0)));
            Assert.That(synced.ServerId, Is.EqualTo(195));
            Assert.That(synced.SyncState, Is.EqualTo(SyncState.Synced));
            Assert.That(_store.Entries, Is.Empty);
        });
    }

    [Test]
    public async Task EntriesForOneProduct_AreSentInOrder_AndTheUpdateUsesTheNewServerId()
    {
        (Product product, OutboxEntry create) = SeedLocalCreate(V1);
        OutboxEntry update = OutboxEntry.New(Guid.NewGuid(), OutboxOperation.Update, product.Id, V2, Now.AddSeconds(1));
        _store.SeedEntry(update);

        MockSequence order = new();
        _api.InSequence(order).Setup(api => api.CreateAsync(V1, create.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServerProduct(7, V1, null, null, null));
        _api.InSequence(order).Setup(api => api.UpdateAsync(7, V2, update.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServerProduct(7, V2, null, null, null));

        SyncRunResult result = await _sync.RunAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Sent, Is.EqualTo(2));
            Assert.That(_store.Product(product.Id)!.SyncState, Is.EqualTo(SyncState.Synced));
        });
    }

    [Test]
    public async Task SentUpdate_KeepsTheServerVersion()
    {
        // DummyJSON's PUT response has no version. If sending an update cleared ServerVersion, the next refresh would
        // treat the server's (unchanged, edit-less) copy as new and overwrite the edit.
        Product product = SeedSynced(serverId: 4);
        OutboxEntry update = SeedEntry(OutboxOperation.Update, product, V2, Now.AddSeconds(-1));
        _api.Setup(api => api.UpdateAsync(4, V2, update.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServerProduct(4, V2, null, null, Version: null));

        await _sync.RunAsync(CancellationToken.None);

        Product after = _store.Product(product.Id)!;
        Assert.Multiple(() =>
        {
            Assert.That(after.ServerVersion, Is.EqualTo("v1"));
            Assert.That(after.ShouldAcceptServerCopy("v1"), Is.False, "a refresh with the same version leaves the edit alone");
        });
    }

    [Test]
    public async Task TemporaryFailure_SchedulesARetryWithBackoff_AndHoldsBackLaterEntriesForThatProduct()
    {
        Product product = SeedSynced(serverId: 4);
        OutboxEntry first = SeedEntry(OutboxOperation.Update, product, V1, Now.AddSeconds(-2));
        SeedEntry(OutboxOperation.Delete, product, null, Now.AddSeconds(-1));
        _api.Setup(api => api.UpdateAsync(4, V1, first.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException(ApiFailureKind.Server, "Service unavailable", 503));

        SyncRunResult result = await _sync.RunAsync(CancellationToken.None);

        OutboxEntry retried = _store.Entry(first.Id)!;
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new SyncRunResult(Sent: 0, Retrying: 1, Failed: 0, Waiting: 1)));
            Assert.That(retried.Status, Is.EqualTo(OutboxStatus.Pending));
            Assert.That(retried.Attempts, Is.EqualTo(1));
            Assert.That(retried.NextAttemptAt, Is.EqualTo(Now + RetryPolicy.DelayFor(1, 0.5)));
            Assert.That(retried.LastError, Does.StartWith("Service unavailable"));
            Assert.That(_store.Product(product.Id)!.SyncState, Is.EqualTo(SyncState.PendingUpdate), "unchanged by a retry");
        });
        _api.Verify(api => api.DeleteAsync(It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task EntryInBackoff_IsNotSentUntilItsTimeComes()
    {
        Product product = SeedSynced(serverId: 4);
        OutboxEntry entry = SeedEntry(OutboxOperation.Update, product, V1, Now.AddMinutes(-1)) with
        {
            Attempts = 1,
            NextAttemptAt = Now.AddSeconds(10),
        };
        _store.SeedEntry(entry);
        _api.Setup(api => api.UpdateAsync(4, V1, entry.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new ServerProduct(4, V1, null, null, null));

        SyncRunResult early = await _sync.RunAsync(CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(10));
        SyncRunResult due = await _sync.RunAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(early, Is.EqualTo(new SyncRunResult(0, 0, 0, Waiting: 1)));
            Assert.That(due.Sent, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task NetworkFailure_StopsTheRun()
    {
        Product a = SeedSynced(serverId: 1);
        Product b = SeedSynced(serverId: 2);
        OutboxEntry first = SeedEntry(OutboxOperation.Update, a, V1, Now.AddSeconds(-2));
        SeedEntry(OutboxOperation.Update, b, V1, Now.AddSeconds(-1));
        _api.Setup(api => api.UpdateAsync(1, V1, first.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException(ApiFailureKind.Network, "Couldn't reach the server."));

        SyncRunResult result = await _sync.RunAsync(CancellationToken.None);

        Assert.That(result, Is.EqualTo(new SyncRunResult(Sent: 0, Retrying: 1, Failed: 0, Waiting: 1)));
        _api.Verify(api => api.UpdateAsync(2, It.IsAny<ProductDetails>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task OtherProducts_CarryOnAfterAServerError()
    {
        Product a = SeedSynced(serverId: 1);
        Product b = SeedSynced(serverId: 2);
        OutboxEntry first = SeedEntry(OutboxOperation.Update, a, V1, Now.AddSeconds(-2));
        OutboxEntry second = SeedEntry(OutboxOperation.Update, b, V1, Now.AddSeconds(-1));
        _api.Setup(api => api.UpdateAsync(1, V1, first.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException(ApiFailureKind.Server, "boom", 500));
        _api.Setup(api => api.UpdateAsync(2, V1, second.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServerProduct(2, V1, null, null, null));

        SyncRunResult result = await _sync.RunAsync(CancellationToken.None);

        Assert.That(result, Is.EqualTo(new SyncRunResult(Sent: 1, Retrying: 1, Failed: 0, Waiting: 0)));
    }

    [Test]
    public async Task PermanentFailure_MarksTheEntryAndProductFailed_AndIsNotRetried()
    {
        Product product = SeedSynced(serverId: 4);
        OutboxEntry entry = SeedEntry(OutboxOperation.Update, product, V1, Now.AddSeconds(-1));
        _api.Setup(api => api.UpdateAsync(4, V1, entry.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException(ApiFailureKind.Rejected, "Price must be positive", 422));

        SyncRunResult first = await _sync.RunAsync(CancellationToken.None);
        SyncRunResult second = await _sync.RunAsync(CancellationToken.None);

        OutboxEntry failed = _store.Entry(entry.Id)!;
        Assert.Multiple(() =>
        {
            Assert.That(first.Failed, Is.EqualTo(1));
            Assert.That(second, Is.EqualTo(new SyncRunResult(0, 0, 0, Waiting: 1)));
            Assert.That(failed.Status, Is.EqualTo(OutboxStatus.Failed));
            Assert.That(failed.LastError, Is.EqualTo("The server rejected the update (422): Price must be positive"));
            Assert.That(_store.Product(product.Id)!.SyncState, Is.EqualTo(SyncState.Failed));
        });
        _api.Verify(api => api.UpdateAsync(It.IsAny<int>(), It.IsAny<ProductDetails>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task UpdateOfAProductTheServerLost_FailsWithAReadableReason()
    {
        // DummyJSON gave the create id 195 but never stored it, so updating it later is a 404.
        Product product = SeedSynced(serverId: 195);
        OutboxEntry entry = SeedEntry(OutboxOperation.Update, product, V1, Now.AddSeconds(-1));
        _api.Setup(api => api.UpdateAsync(195, V1, entry.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException(ApiFailureKind.NotFound, "Product with id '195' not found", 404));

        await _sync.RunAsync(CancellationToken.None);

        Assert.That(_store.Entry(entry.Id)!.LastError, Does.StartWith("The server has no product #195").And.Contains("DummyJSON"));
    }

    [Test]
    public async Task Delete_ServerSays404_CountsAsDone()
    {
        Product product = SeedSynced(serverId: 195);
        product.ApplyLocalDelete();
        _store.Seed(product);
        OutboxEntry entry = SeedEntry(OutboxOperation.Delete, product, null, Now.AddSeconds(-1));
        _api.Setup(api => api.DeleteAsync(195, entry.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException(ApiFailureKind.NotFound, "not found", 404));

        SyncRunResult result = await _sync.RunAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Sent, Is.EqualTo(1));
            Assert.That(_store.Entries, Is.Empty);
            Assert.That(_store.Product(product.Id)!.SyncState, Is.EqualTo(SyncState.Synced));
            Assert.That(_store.Product(product.Id)!.IsDeleted, Is.True);
        });
    }

    [Test]
    public async Task Delete_OfAProductTheServerNeverHad_CompletesWithoutARequest()
    {
        Product product = Product.CreateLocal(Guid.NewGuid(), V1);
        product.ApplyLocalDelete();
        _store.Seed(product);
        SeedEntry(OutboxOperation.Delete, product, null, Now.AddSeconds(-1));

        SyncRunResult result = await _sync.RunAsync(CancellationToken.None);

        Assert.That(result.Sent, Is.EqualTo(1));
        _api.VerifyNoOtherCalls();
    }

    [Test]
    public async Task EditMadeWhileTheCreateIsInFlight_LeavesTheProductPendingUpdate()
    {
        (Product product, OutboxEntry create) = SeedLocalCreate(V1);
        _api.Setup(api => api.CreateAsync(V1, create.Id, It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                // The user saves an edit while the create's request is out: it queues a new entry behind it.
                _store.SeedEntry(OutboxEntry.New(Guid.NewGuid(), OutboxOperation.Update, product.Id, V2, Now.AddSeconds(1)));
            })
            .ReturnsAsync(new ServerProduct(195, V1, null, null, null));
        _api.Setup(api => api.UpdateAsync(195, V2, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException(ApiFailureKind.Network, "offline"));

        await _sync.RunAsync(CancellationToken.None);

        Product after = _store.Product(product.Id)!;
        Assert.Multiple(() =>
        {
            Assert.That(after.ServerId, Is.EqualTo(195));
            Assert.That(after.SyncState, Is.EqualTo(SyncState.PendingUpdate), "not Synced: an edit is still queued");
        });
    }

    [Test]
    public async Task EntryChangedAfterTheRunStarted_IsSentWithItsLatestPayload()
    {
        Product a = SeedSynced(serverId: 1);
        Product b = SeedSynced(serverId: 2);
        OutboxEntry first = SeedEntry(OutboxOperation.Update, a, V1, Now.AddSeconds(-2));
        OutboxEntry second = SeedEntry(OutboxOperation.Update, b, V1, Now.AddSeconds(-1));

        _api.Setup(api => api.UpdateAsync(1, V1, first.Id, It.IsAny<CancellationToken>()))
            .Callback(() => _store.SeedEntry(second with { Payload = V2 }))
            .ReturnsAsync(new ServerProduct(1, V1, null, null, null));
        _api.Setup(api => api.UpdateAsync(2, V2, second.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServerProduct(2, V2, null, null, null));

        SyncRunResult result = await _sync.RunAsync(CancellationToken.None);

        Assert.That(result.Sent, Is.EqualTo(2));
        _api.Verify(api => api.UpdateAsync(2, V1, It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task EntryRemovedAfterTheRunStarted_IsNotSent()
    {
        Product a = SeedSynced(serverId: 1);
        (Product b, OutboxEntry bCreate) = SeedLocalCreate(V1, Now.AddSeconds(-1));
        OutboxEntry first = SeedEntry(OutboxOperation.Update, a, V1, Now.AddSeconds(-2));

        _api.Setup(api => api.UpdateAsync(1, V1, first.Id, It.IsAny<CancellationToken>()))
            .Callback(() => _store.CommitAsync([], [], [bCreate.Id], CancellationToken.None))
            .ReturnsAsync(new ServerProduct(1, V1, null, null, null));

        SyncRunResult result = await _sync.RunAsync(CancellationToken.None);

        Assert.That(result.Sent, Is.EqualTo(1));
        _api.Verify(api => api.CreateAsync(It.IsAny<ProductDetails>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task OverlappingRun_IsSkipped()
    {
        (_, OutboxEntry create) = SeedLocalCreate(V1);
        TaskCompletionSource<ServerProduct> response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _api.Setup(api => api.CreateAsync(V1, create.Id, It.IsAny<CancellationToken>())).Returns(response.Task);

        Task<SyncRunResult> firstRun = _sync.RunAsync(CancellationToken.None);
        SyncRunResult overlapping = await _sync.RunAsync(CancellationToken.None);
        response.SetResult(new ServerProduct(195, V1, null, null, null));
        SyncRunResult first = await firstRun;

        Assert.Multiple(() =>
        {
            Assert.That(overlapping.Skipped, Is.True);
            Assert.That(first.Sent, Is.EqualTo(1));
        });
        _api.Verify(api => api.CreateAsync(V1, create.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void CancelledMidRequest_PutsTheEntryBackToPending()
    {
        (_, OutboxEntry create) = SeedLocalCreate(V1);
        using CancellationTokenSource cts = new();
        _api.Setup(api => api.CreateAsync(V1, create.Id, It.IsAny<CancellationToken>()))
            .Returns(async (ProductDetails _, Guid _, CancellationToken token) =>
            {
                await cts.CancelAsync();
                token.ThrowIfCancellationRequested();
                return new ServerProduct(195, V1, null, null, null);
            });

        Assert.Multiple(() =>
        {
            Assert.That(() => _sync.RunAsync(cts.Token), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(_store.Entry(create.Id)!.Status, Is.EqualTo(OutboxStatus.Pending));
        });
    }

    [Test]
    public async Task RecoverAsync_ReturnsInFlightEntriesToPending()
    {
        (_, OutboxEntry create) = SeedLocalCreate(V1);
        _store.SeedEntry(create with { Status = OutboxStatus.InFlight });

        await _sync.RecoverAsync(CancellationToken.None);

        Assert.That(_store.Entry(create.Id)!.Status, Is.EqualTo(OutboxStatus.Pending));
    }

    [Test]
    public async Task RetryAsync_MakesAFailedEntryPendingAgain()
    {
        Product product = SeedSynced(serverId: 4);
        product.SetSyncState(SyncState.Failed);
        _store.Seed(product);
        OutboxEntry failed = SeedEntry(OutboxOperation.Update, product, V1, Now.AddSeconds(-1)) with
        {
            Status = OutboxStatus.Failed,
            Attempts = 2,
            LastError = "rejected",
        };
        _store.SeedEntry(failed);

        await _sync.RetryAsync(failed.Id, CancellationToken.None);

        OutboxEntry retried = _store.Entry(failed.Id)!;
        Assert.Multiple(() =>
        {
            Assert.That(retried.Status, Is.EqualTo(OutboxStatus.Pending));
            Assert.That(retried.Attempts, Is.Zero);
            Assert.That(retried.LastError, Is.Null);
            Assert.That(_store.Product(product.Id)!.SyncState, Is.EqualTo(SyncState.PendingUpdate));
        });
    }

    [Test]
    public void RetryAsync_EntryThatHasNotFailed_Throws()
    {
        (_, OutboxEntry create) = SeedLocalCreate(V1);

        Assert.That(() => _sync.RetryAsync(create.Id, CancellationToken.None), Throws.InvalidOperationException);
    }

    [Test]
    public async Task DiscardAsync_FailedCreate_RemovesTheProductAndEverythingQueuedAfterIt()
    {
        (Product product, OutboxEntry create) = SeedLocalCreate(V1);
        _store.SeedEntry(create with { Status = OutboxStatus.Failed });
        SeedEntry(OutboxOperation.Delete, product, null, Now.AddSeconds(1));

        Guid entityId = await _sync.DiscardAsync(create.Id, CancellationToken.None);

        Product after = _store.Product(product.Id)!;
        Assert.Multiple(() =>
        {
            Assert.That(entityId, Is.EqualTo(product.Id));
            Assert.That(_store.Entries, Is.Empty);
            Assert.That(after.IsDeleted, Is.True);
            Assert.That(after.SyncState, Is.EqualTo(SyncState.Synced));
        });
    }

    [Test]
    public async Task DiscardAsync_FailedUpdate_LetsTheServerCopyWinNextTime()
    {
        Product product = SeedSynced(serverId: 4);
        OutboxEntry failed = SeedEntry(OutboxOperation.Update, product, V2, Now.AddSeconds(-1)) with { Status = OutboxStatus.Failed };
        _store.SeedEntry(failed);

        await _sync.DiscardAsync(failed.Id, CancellationToken.None);

        Product after = _store.Product(product.Id)!;
        Assert.Multiple(() =>
        {
            Assert.That(_store.Entries, Is.Empty);
            Assert.That(after.SyncState, Is.EqualTo(SyncState.Synced));
            Assert.That(after.ServerVersion, Is.Null);
            Assert.That(after.ShouldAcceptServerCopy("v1"), Is.True, "even the version it was based on now replaces it");
        });
    }

    [Test]
    public async Task DiscardAsync_FailedDelete_BringsTheProductBack()
    {
        Product product = SeedSynced(serverId: 4);
        product.ApplyLocalDelete();
        _store.Seed(product);
        OutboxEntry failed = SeedEntry(OutboxOperation.Delete, product, null, Now.AddSeconds(-1)) with { Status = OutboxStatus.Failed };
        _store.SeedEntry(failed);

        await _sync.DiscardAsync(failed.Id, CancellationToken.None);

        Product after = _store.Product(product.Id)!;
        Assert.Multiple(() =>
        {
            Assert.That(after.IsDeleted, Is.False);
            Assert.That(after.SyncState, Is.EqualTo(SyncState.Synced));
            Assert.That(after.ServerVersion, Is.Null);
        });
    }

    [Test]
    public async Task ClearLocalDataAsync_EmptiesTheStore()
    {
        SeedLocalCreate(V1);

        await _sync.ClearLocalDataAsync(CancellationToken.None);

        Assert.That(_store.Entries, Is.Empty);
    }

    [Test]
    public async Task ClearLocalDataAsync_WaitsForARunInProgress()
    {
        (Product product, OutboxEntry create) = SeedLocalCreate(V1);
        TaskCompletionSource<ServerProduct> response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _api.Setup(api => api.CreateAsync(V1, create.Id, It.IsAny<CancellationToken>())).Returns(response.Task);

        Task<SyncRunResult> run = _sync.RunAsync(CancellationToken.None);
        Task clear = _sync.ClearLocalDataAsync(CancellationToken.None);
        bool clearedEarly = clear.IsCompleted;
        response.SetResult(new ServerProduct(195, V1, null, null, null));
        await run;
        await clear;

        Assert.Multiple(() =>
        {
            Assert.That(clearedEarly, Is.False, "clearing waited for the run");
            Assert.That(_store.Product(product.Id), Is.Null, "and the run's write-back didn't survive the clear");
        });
    }

    [Test]
    public async Task GetOutboxItemsAsync_UsesTheTitleOnThisDevice_EvenForATombstone()
    {
        Product product = SeedSynced(serverId: 4);
        product.ApplyLocalDelete();
        _store.Seed(product);
        SeedEntry(OutboxOperation.Delete, product, null, Now);

        OutboxItem item = (await _sync.GetOutboxItemsAsync(CancellationToken.None)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(item.ProductTitle, Is.EqualTo("Version 1"));
            Assert.That(item.ServerId, Is.EqualTo(4));
        });
    }

    private (Product Product, OutboxEntry Create) SeedLocalCreate(ProductDetails details, DateTimeOffset? createdAt = null)
    {
        Product product = Product.CreateLocal(Guid.NewGuid(), details);
        OutboxEntry create = OutboxEntry.New(Guid.NewGuid(), OutboxOperation.Create, product.Id, details, createdAt ?? Now.AddSeconds(-5));
        _store.Seed(product, create);
        return (product, create);
    }

    private Product SeedSynced(int serverId)
    {
        Product product = Product.Restore(Guid.NewGuid(), V1, null, null, serverId, "v1", SyncState.PendingUpdate, isDeleted: false);
        _store.Seed(product);
        return product;
    }

    private OutboxEntry SeedEntry(OutboxOperation operation, Product product, ProductDetails? payload, DateTimeOffset createdAt)
    {
        OutboxEntry entry = OutboxEntry.New(Guid.NewGuid(), operation, product.Id, payload, createdAt);
        _store.SeedEntry(entry);
        return entry;
    }

    private sealed class FixedRandom(double value) : Random
    {
        public override double NextDouble() => value;
    }
}
