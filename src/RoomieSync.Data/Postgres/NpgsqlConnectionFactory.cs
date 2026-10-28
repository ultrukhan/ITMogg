using System.Diagnostics.CodeAnalysis;
using Npgsql;

namespace RoomieSync.Data.Postgres;

public interface IDbConnectionFactory
{
    Task<NpgsqlConnection> OpenAsync(CancellationToken ct = default);
}

public sealed class NpgsqlConnectionFactory : IDbConnectionFactory, IDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public NpgsqlConnectionFactory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Connection string 'RoomieSync' is not configured (appsettings.json → ConnectionStrings:RoomieSync).");
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    [ExcludeFromCodeCoverage]
    public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct = default) => await _dataSource.OpenConnectionAsync(ct);

    public void Dispose() => _dataSource.Dispose();
}

internal static class DbExtensions
{
    public static NpgsqlCommand Cmd(this NpgsqlConnection conn, string sql, NpgsqlTransaction? tx = null)
        => new(sql, conn, tx);

    public static NpgsqlCommand P(this NpgsqlCommand cmd, string name, object? value)
    {
        cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    [ExcludeFromCodeCoverage]
    public static T? GetNullable<T>(this NpgsqlDataReader r, string column)
    {
        var i = r.GetOrdinal(column);
        return r.IsDBNull(i) ? default : r.GetFieldValue<T>(i);
    }
}
