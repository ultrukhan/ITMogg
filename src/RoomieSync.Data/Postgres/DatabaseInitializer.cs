using System.Reflection;

namespace RoomieSync.Data.Postgres;

public sealed class DatabaseInitializer(IDbConnectionFactory factory)
{
    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RoomieSync.Data.schema.sql")
            ?? throw new InvalidOperationException("Embedded schema.sql not found.");
        using var reader = new StreamReader(stream);
        var sql = await reader.ReadToEndAsync(ct);
        await using var conn = await factory.OpenAsync(ct);
        await using var cmd = conn.Cmd(sql);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
