using System.Text.Json;
using OfflineCrud.Application.Sync;
using OfflineCrud.Domain.Products;
using OfflineCrud.Infrastructure.IndexedDb;

namespace OfflineCrud.Infrastructure.Tests.IndexedDb;

[TestFixture]
public class OutboxRecordMapperTests
{
    private static readonly JsonSerializerOptions InteropOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public void Entry_RoundTripsThroughTheJsShape()
    {
        OutboxEntry original = new(
            Guid.NewGuid(),
            OutboxOperation.Update,
            Guid.NewGuid(),
            new ProductDetails("Title", "Description", "beauty", "Brand", 12.34m, 5m, 7),
            new DateTimeOffset(2026, 10, 17, 12, 30, 15, 123, TimeSpan.Zero),
            Attempts: 2,
            NextAttemptAt: new DateTimeOffset(2026, 10, 17, 12, 31, 0, TimeSpan.Zero),
            LastError: "Service unavailable",
            OutboxStatus.InFlight);

        string json = JsonSerializer.Serialize(OutboxRecordMapper.ToRecord(original), InteropOptions);
        OutboxEntry restored = OutboxRecordMapper.ToEntry(JsonSerializer.Deserialize<OutboxRecord>(json, InteropOptions)!);

        Assert.That(restored, Is.EqualTo(original));
    }

    [Test]
    public void Delete_HasNoPayload()
    {
        OutboxEntry delete = OutboxEntry.New(Guid.NewGuid(), OutboxOperation.Delete, Guid.NewGuid(), null, DateTimeOffset.UtcNow);

        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(OutboxRecordMapper.ToRecord(delete), InteropOptions));

        Assert.That(json.RootElement.GetProperty("payload").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public void CreatedAt_IsStoredAsSortableUtcText()
    {
        // The byCreated index sorts these strings, so they must be in one offset and a fixed-width format.
        OutboxEntry earlier = OutboxEntry.New(Guid.NewGuid(), OutboxOperation.Delete, Guid.NewGuid(), null,
            new DateTimeOffset(2026, 10, 17, 13, 0, 0, TimeSpan.FromHours(2)));
        OutboxEntry later = earlier with { CreatedAt = new DateTimeOffset(2026, 10, 17, 11, 30, 0, TimeSpan.Zero) };

        string earlierText = OutboxRecordMapper.ToRecord(earlier).CreatedAt;
        string laterText = OutboxRecordMapper.ToRecord(later).CreatedAt;

        Assert.Multiple(() =>
        {
            Assert.That(earlierText, Does.EndWith("+00:00"));
            Assert.That(string.CompareOrdinal(earlierText, laterText), Is.LessThan(0));
        });
    }
}
