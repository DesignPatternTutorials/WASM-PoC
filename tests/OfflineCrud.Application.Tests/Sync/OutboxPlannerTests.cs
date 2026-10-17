using OfflineCrud.Application.Sync;
using OfflineCrud.Application.Tests.Domain;
using OfflineCrud.Domain.Products;

namespace OfflineCrud.Application.Tests.Sync;

[TestFixture]
public class OutboxPlannerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Entity = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly ProductDetails V1 = ProductDetailsTests.Create(title: "Version 1");
    private static readonly ProductDetails V2 = ProductDetailsTests.Create(title: "Version 2");

    [Test]
    public void Create_OnEmptyQueue_AddsOneCreate()
    {
        OutboxPlan plan = Plan([], OutboxOperation.Create, V1);

        OutboxEntry created = plan.ToSave.Single();
        Assert.Multiple(() =>
        {
            Assert.That(created.Operation, Is.EqualTo(OutboxOperation.Create));
            Assert.That(created.Payload, Is.EqualTo(V1));
            Assert.That(created.Status, Is.EqualTo(OutboxStatus.Pending));
            Assert.That(created.CreatedAt, Is.EqualTo(T0));
            Assert.That(plan.ToDelete, Is.Empty);
            Assert.That(plan.SyncState, Is.EqualTo(SyncState.PendingCreate));
        });
    }

    [Test]
    public void Create_WhenAlreadyQueued_Throws()
    {
        Assert.That(() => Plan([Entry(OutboxOperation.Create, V1)], OutboxOperation.Create, V2), Throws.InvalidOperationException);
    }

    [Test]
    public void CreateThenUpdate_BecomesOneCreateWithTheLatestDetails()
    {
        OutboxEntry create = Entry(OutboxOperation.Create, V1);

        OutboxPlan plan = Plan([create], OutboxOperation.Update, V2);

        OutboxEntry merged = plan.ToSave.Single();
        Assert.Multiple(() =>
        {
            Assert.That(merged.Id, Is.EqualTo(create.Id), "the existing entry is changed, not replaced");
            Assert.That(merged.Operation, Is.EqualTo(OutboxOperation.Create));
            Assert.That(merged.Payload, Is.EqualTo(V2));
            Assert.That(merged.CreatedAt, Is.EqualTo(create.CreatedAt), "it keeps its place in the queue");
            Assert.That(plan.Queue, Has.Count.EqualTo(1));
            Assert.That(plan.SyncState, Is.EqualTo(SyncState.PendingCreate));
        });
    }

    [Test]
    public void UpdateThenUpdate_KeepsOnlyTheLatest()
    {
        OutboxEntry update = Entry(OutboxOperation.Update, V1);

        OutboxPlan plan = Plan([update], OutboxOperation.Update, V2);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Queue.Single().Payload, Is.EqualTo(V2));
            Assert.That(plan.Queue.Single().Id, Is.EqualTo(update.Id));
            Assert.That(plan.SyncState, Is.EqualTo(SyncState.PendingUpdate));
        });
    }

    [Test]
    public void Update_OnEmptyQueue_AddsAnUpdate()
    {
        OutboxPlan plan = Plan([], OutboxOperation.Update, V1);

        Assert.Multiple(() =>
        {
            Assert.That(plan.ToSave.Single().Operation, Is.EqualTo(OutboxOperation.Update));
            Assert.That(plan.SyncState, Is.EqualTo(SyncState.PendingUpdate));
        });
    }

    [Test]
    public void Update_WhileTheLastEntryIsInFlight_QueuesBehindIt()
    {
        // Part of an in-flight request may already have reached the server, so it must not be changed.
        OutboxEntry inFlight = Entry(OutboxOperation.Create, V1) with { Status = OutboxStatus.InFlight };

        OutboxPlan plan = Plan([inFlight], OutboxOperation.Update, V2);

        Assert.Multiple(() =>
        {
            Assert.That(plan.ToSave.Single().Id, Is.Not.EqualTo(inFlight.Id));
            Assert.That(plan.Queue.Select(entry => entry.Operation), Is.EqualTo(new[] { OutboxOperation.Create, OutboxOperation.Update }));
            Assert.That(inFlight.Payload, Is.EqualTo(V1));
            Assert.That(plan.SyncState, Is.EqualTo(SyncState.PendingUpdate));
        });
    }

    [Test]
    public void Update_IntoAFailedEntry_MakesItPendingAgainWithAFreshRetryHistory()
    {
        OutboxEntry failed = Entry(OutboxOperation.Update, V1) with
        {
            Status = OutboxStatus.Failed,
            Attempts = 3,
            NextAttemptAt = T0.AddMinutes(5),
            LastError = "rejected",
        };

        OutboxPlan plan = Plan([failed], OutboxOperation.Update, V2);

        OutboxEntry retried = plan.ToSave.Single();
        Assert.Multiple(() =>
        {
            Assert.That(retried.Status, Is.EqualTo(OutboxStatus.Pending));
            Assert.That(retried.Attempts, Is.Zero);
            Assert.That(retried.NextAttemptAt, Is.Null);
            Assert.That(retried.LastError, Is.Null);
            Assert.That(retried.Payload, Is.EqualTo(V2));
            Assert.That(plan.SyncState, Is.EqualTo(SyncState.PendingUpdate));
        });
    }

    [Test]
    public void Update_WhileADeleteIsQueued_Throws()
    {
        Assert.That(() => Plan([Entry(OutboxOperation.Delete, null)], OutboxOperation.Update, V2), Throws.InvalidOperationException);
    }

    [Test]
    public void CreateThenDelete_BeforeSending_DropsBoth()
    {
        OutboxEntry create = Entry(OutboxOperation.Create, V1);

        OutboxPlan plan = Plan([create], OutboxOperation.Delete, null);

        Assert.Multiple(() =>
        {
            Assert.That(plan.ToSave, Is.Empty);
            Assert.That(plan.ToDelete, Is.EqualTo(new[] { create.Id }));
            Assert.That(plan.Queue, Is.Empty);
            Assert.That(plan.SyncState, Is.EqualTo(SyncState.Synced), "nothing left to tell the server");
        });
    }

    [Test]
    public void Delete_WhileTheCreateIsInFlight_QueuesADelete()
    {
        OutboxEntry inFlight = Entry(OutboxOperation.Create, V1) with { Status = OutboxStatus.InFlight };

        OutboxPlan plan = Plan([inFlight], OutboxOperation.Delete, null);

        Assert.Multiple(() =>
        {
            Assert.That(plan.ToDelete, Is.Empty);
            Assert.That(plan.ToSave.Single().Operation, Is.EqualTo(OutboxOperation.Delete));
            Assert.That(plan.SyncState, Is.EqualTo(SyncState.PendingDelete));
        });
    }

    [Test]
    public void UpdateThenDelete_BecomesJustTheDelete()
    {
        OutboxEntry update = Entry(OutboxOperation.Update, V1);

        OutboxPlan plan = Plan([update], OutboxOperation.Delete, null);

        OutboxEntry delete = plan.Queue.Single();
        Assert.Multiple(() =>
        {
            Assert.That(delete.Id, Is.EqualTo(update.Id));
            Assert.That(delete.Operation, Is.EqualTo(OutboxOperation.Delete));
            Assert.That(delete.Payload, Is.Null);
            Assert.That(plan.SyncState, Is.EqualTo(SyncState.PendingDelete));
        });
    }

    [Test]
    public void DeleteTwice_ChangesNothing()
    {
        OutboxEntry delete = Entry(OutboxOperation.Delete, null);

        OutboxPlan plan = Plan([delete], OutboxOperation.Delete, null);

        Assert.Multiple(() =>
        {
            Assert.That(plan.ToSave, Is.Empty);
            Assert.That(plan.ToDelete, Is.Empty);
            Assert.That(plan.Queue, Is.EqualTo(new[] { delete }));
        });
    }

    [Test]
    public void Delete_OnEmptyQueue_AddsADelete()
    {
        OutboxPlan plan = Plan([], OutboxOperation.Delete, null);

        Assert.That(plan.ToSave.Single().Operation, Is.EqualTo(OutboxOperation.Delete));
    }

    [Test]
    public void DeriveSyncState_AnyFailedEntry_IsFailed()
    {
        OutboxEntry failed = Entry(OutboxOperation.Create, V1) with { Status = OutboxStatus.Failed };
        OutboxEntry later = Entry(OutboxOperation.Update, V2, T0.AddSeconds(1));

        Assert.That(OutboxPlanner.DeriveSyncState([failed, later]), Is.EqualTo(SyncState.Failed));
    }

    [Test]
    public void DeriveSyncState_UsesTheMostRecentEntry()
    {
        OutboxEntry create = Entry(OutboxOperation.Create, V1, T0) with { Status = OutboxStatus.InFlight };
        OutboxEntry delete = Entry(OutboxOperation.Delete, null, T0.AddSeconds(1));

        Assert.That(OutboxPlanner.DeriveSyncState([delete, create]), Is.EqualTo(SyncState.PendingDelete));
    }

    [Test]
    public void InSendOrder_OrdersByTimeThenId()
    {
        OutboxEntry late = Entry(OutboxOperation.Update, V1, T0.AddSeconds(1));
        OutboxEntry tieB = Entry(OutboxOperation.Update, V1, T0) with { Id = Guid.Parse("00000000-0000-0000-0000-00000000000b") };
        OutboxEntry tieA = Entry(OutboxOperation.Update, V1, T0) with { Id = Guid.Parse("00000000-0000-0000-0000-00000000000a") };

        Assert.That(OutboxPlanner.InSendOrder([late, tieB, tieA]), Is.EqualTo(new[] { tieA, tieB, late }));
    }

    private static OutboxPlan Plan(IReadOnlyList<OutboxEntry> existing, OutboxOperation operation, ProductDetails? payload)
    {
        return OutboxPlanner.Plan(existing, operation, Entity, payload, T0, Guid.NewGuid);
    }

    private static OutboxEntry Entry(OutboxOperation operation, ProductDetails? payload, DateTimeOffset? createdAt = null)
    {
        return OutboxEntry.New(Guid.NewGuid(), operation, Entity, payload, createdAt ?? T0.AddMinutes(-1));
    }
}
