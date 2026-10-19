using Microsoft.EntityFrameworkCore;

namespace DepPilot.Master.Data;

public static class DatabaseSetup
{
    public static async Task InitializeAsync(RegistryDb db)
    {
        await db.Database.EnsureCreatedAsync();
        await db.Database.OpenConnectionAsync();
        try
        {
            // Additive upgrade for the original database; never drop data.
            using var inspect = db.Database.GetDbConnection().CreateCommand();
            inspect.CommandText = "PRAGMA table_info('Hosts')";
            var columns = new HashSet<string>();
            await using (var reader = await inspect.ExecuteReaderAsync())
                while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
            foreach (var (name, definition) in new[] {
                ("IpAddress", "TEXT NOT NULL DEFAULT ''"), ("CapabilitiesJson", "TEXT NOT NULL DEFAULT '[]'"),
                ("MetricsJson", "TEXT NULL"), ("DeregisteredAt", "INTEGER NULL") })
            {
                if (columns.Contains(name)) continue;
                using var alter = db.Database.GetDbConnection().CreateCommand();
                alter.CommandText = $"ALTER TABLE Hosts ADD COLUMN {name} {definition}";
                await alter.ExecuteNonQueryAsync();
            }
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}
