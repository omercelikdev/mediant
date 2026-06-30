using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.Audit;

namespace Qorpe.Mediator.EntityFrameworkCore;

/// <summary>
/// <see cref="ModelBuilder"/> extensions that map the Qorpe outbox and audit entities. Call these
/// from your <c>DbContext.OnModelCreating</c>.
/// </summary>
public static class ModelBuilderExtensions
{
    /// <summary>Maps <see cref="OutboxMessage"/> for the transactional outbox.</summary>
    public static ModelBuilder ConfigureQorpeOutbox(this ModelBuilder modelBuilder, string tableName = "QorpeOutboxMessages")
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable(tableName);
            entity.HasKey(e => e.Id);
            entity.Property(e => e.NotificationType).IsRequired();
            entity.Property(e => e.Payload).IsRequired();
            entity.HasIndex(e => e.ProcessedOn);
            entity.HasIndex(e => e.OccurredOn);
        });

        return modelBuilder;
    }

    /// <summary>Maps <see cref="AuditEntry"/> for the durable audit store.</summary>
    public static ModelBuilder ConfigureQorpeAudit(this ModelBuilder modelBuilder, string tableName = "QorpeAuditEntries")
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var metadataComparer = new ValueComparer<Dictionary<string, string>>(
            (a, b) => DictionaryEquals(a, b),
            d => d == null ? 0 : d.Aggregate(0, (hash, kv) => HashCode.Combine(hash, kv.Key, kv.Value)),
            d => d == null ? new Dictionary<string, string>(StringComparer.Ordinal) : new Dictionary<string, string>(d, StringComparer.Ordinal));

        modelBuilder.Entity<AuditEntry>(entity =>
        {
            entity.ToTable(tableName);
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RequestType).IsRequired();
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.CorrelationId);

            // Persist the metadata dictionary as a JSON column.
            var property = entity.Property(e => e.Metadata).HasConversion(
                v => Serialize(v),
                v => Deserialize(v));
            property.Metadata.SetValueComparer(metadataComparer);
        });

        return modelBuilder;
    }

    private static bool DictionaryEquals(Dictionary<string, string>? a, Dictionary<string, string>? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null || a.Count != b.Count)
        {
            return false;
        }

        foreach (var kv in a)
        {
            if (!b.TryGetValue(kv.Key, out var other) || !string.Equals(other, kv.Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string Serialize(Dictionary<string, string> value)
        => JsonSerializer.Serialize(value, MetadataSerializerOptions);

    private static Dictionary<string, string> Deserialize(string value)
        => string.IsNullOrEmpty(value)
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : JsonSerializer.Deserialize<Dictionary<string, string>>(value, MetadataSerializerOptions) ?? new Dictionary<string, string>(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions MetadataSerializerOptions = new(JsonSerializerDefaults.General);
}
