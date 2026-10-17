using System.Globalization;
using OfflineCrud.Application.Sync;
using OfflineCrud.Domain.Products;

namespace OfflineCrud.Infrastructure.IndexedDb;

// The outbox entry as stored in IndexedDB. Times are ISO 8601 strings in UTC: JS has no DateTimeOffset, and in one fixed
// offset the strings sort in time order, which the byCreated index relies on.

internal sealed record OutboxRecord(
    Guid Id,
    string Operation,
    Guid EntityId,
    OutboxPayloadRecord? Payload,
    string CreatedAt,
    int Attempts,
    string? NextAttemptAt,
    string? LastError,
    string Status);

internal sealed record OutboxPayloadRecord(
    string Title,
    string Description,
    string Category,
    string? Brand,
    decimal Price,
    decimal DiscountPercentage,
    int Stock);

/// <summary>Converts between outbox entries and the plain records stored in IndexedDB.</summary>
internal static class OutboxRecordMapper
{
    public static OutboxRecord ToRecord(OutboxEntry entry)
    {
        return new OutboxRecord(
            entry.Id,
            entry.Operation.ToString(),
            entry.EntityId,
            entry.Payload is { } payload
                ? new OutboxPayloadRecord(payload.Title, payload.Description, payload.Category, payload.Brand, payload.Price, payload.DiscountPercentage, payload.Stock)
                : null,
            FormatTime(entry.CreatedAt),
            entry.Attempts,
            entry.NextAttemptAt is { } next ? FormatTime(next) : null,
            entry.LastError,
            entry.Status.ToString());
    }

    public static OutboxEntry ToEntry(OutboxRecord record)
    {
        return new OutboxEntry(
            record.Id,
            Enum.Parse<OutboxOperation>(record.Operation),
            record.EntityId,
            record.Payload is { } payload
                ? new ProductDetails(payload.Title, payload.Description, payload.Category, payload.Brand, payload.Price, payload.DiscountPercentage, payload.Stock)
                : null,
            ParseTime(record.CreatedAt),
            record.Attempts,
            record.NextAttemptAt is { } next ? ParseTime(next) : null,
            record.LastError,
            Enum.Parse<OutboxStatus>(record.Status));
    }

    private static string FormatTime(DateTimeOffset time) => time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTime(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
