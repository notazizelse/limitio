using Microsoft.Data.Sqlite;
using LimitIO.Core.Models;

namespace LimitIO.Core.Storage;

/// <summary>
/// SQLite-backed per-rule/per-day usage accounting. SQLite (rather than a flat JSON file) was chosen
/// because usage accounting is many small, frequent writes from a background timer happening
/// concurrently with reads from IPC request handling — exactly the read/write-under-concurrency shape
/// SQLite's own locking handles safely, where a single hand-rolled JSON file would need its own
/// reader/writer coordination to avoid a torn read or a lost update.
/// </summary>
public sealed class UsageStore : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly object _lock = new();

    public UsageStore(string dbPath)
    {
        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS UsageRecords (
                RuleId TEXT NOT NULL,
                Date TEXT NOT NULL,
                ConsumedTicks INTEGER NOT NULL,
                GraceUsedToday INTEGER NOT NULL,
                PRIMARY KEY (RuleId, Date)
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public UsageRecord GetOrCreate(Guid ruleId, DateOnly date)
    {
        lock (_lock)
        {
            using var select = _connection.CreateCommand();
            select.CommandText = "SELECT ConsumedTicks, GraceUsedToday FROM UsageRecords WHERE RuleId = $ruleId AND Date = $date";
            select.Parameters.AddWithValue("$ruleId", ruleId.ToString());
            select.Parameters.AddWithValue("$date", date.ToString("O"));

            using var reader = select.ExecuteReader();
            if (reader.Read())
            {
                return new UsageRecord
                {
                    RuleId = ruleId,
                    Date = date,
                    Consumed = TimeSpan.FromTicks(reader.GetInt64(0)),
                    GraceUsedToday = reader.GetInt64(1) != 0,
                };
            }

            return new UsageRecord { RuleId = ruleId, Date = date };
        }
    }

    public void Save(UsageRecord record)
    {
        lock (_lock)
        {
            using var upsert = _connection.CreateCommand();
            upsert.CommandText = """
                INSERT INTO UsageRecords (RuleId, Date, ConsumedTicks, GraceUsedToday)
                VALUES ($ruleId, $date, $consumed, $grace)
                ON CONFLICT(RuleId, Date) DO UPDATE SET
                    ConsumedTicks = excluded.ConsumedTicks,
                    GraceUsedToday = excluded.GraceUsedToday;
                """;
            upsert.Parameters.AddWithValue("$ruleId", record.RuleId.ToString());
            upsert.Parameters.AddWithValue("$date", record.Date.ToString("O"));
            upsert.Parameters.AddWithValue("$consumed", record.Consumed.Ticks);
            upsert.Parameters.AddWithValue("$grace", record.GraceUsedToday ? 1 : 0);
            upsert.ExecuteNonQuery();
        }
    }

    /// <summary>All usage records for a given date, keyed by rule id — used to build the UI's status view in one query.</summary>
    public IReadOnlyDictionary<Guid, UsageRecord> GetAllForDate(DateOnly date)
    {
        lock (_lock)
        {
            var result = new Dictionary<Guid, UsageRecord>();
            using var select = _connection.CreateCommand();
            select.CommandText = "SELECT RuleId, ConsumedTicks, GraceUsedToday FROM UsageRecords WHERE Date = $date";
            select.Parameters.AddWithValue("$date", date.ToString("O"));

            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                var ruleId = Guid.Parse(reader.GetString(0));
                result[ruleId] = new UsageRecord
                {
                    RuleId = ruleId,
                    Date = date,
                    Consumed = TimeSpan.FromTicks(reader.GetInt64(1)),
                    GraceUsedToday = reader.GetInt64(2) != 0,
                };
            }
            return result;
        }
    }

    /// <summary>Deletes usage rows older than <paramref name="retain"/> days, so the database doesn't grow forever.</summary>
    public void PruneOlderThan(DateOnly cutoff)
    {
        lock (_lock)
        {
            using var delete = _connection.CreateCommand();
            delete.CommandText = "DELETE FROM UsageRecords WHERE Date < $cutoff";
            delete.Parameters.AddWithValue("$cutoff", cutoff.ToString("O"));
            delete.ExecuteNonQuery();
        }
    }

    public void Dispose() => _connection.Dispose();
}
