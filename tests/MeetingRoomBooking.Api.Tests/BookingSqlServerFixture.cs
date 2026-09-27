using Microsoft.Data.SqlClient;

namespace MeetingRoomBooking.Api.Tests;

/// <summary>
/// Creates a uniquely-named SQL Server database for one test class's run,
/// enables READ_COMMITTED_SNAPSHOT on it (matching Azure SQL — see
/// docs/adr/0001-booking-concurrency.md), points a <see cref="BookingsApiFactory"/>
/// at it, and drops it afterwards. EF InMemory is explicitly not used here:
/// this exists specifically to exercise real unique-index enforcement and
/// RCSI behavior, neither of which InMemory can reproduce.
/// </summary>
/// <remarks>
/// Connection string comes from the <c>ConnectionStrings__DefaultConnection</c>
/// environment variable if set (CI's SQL Server service container sets
/// this — see .github/workflows/backend.yml), otherwise a LocalDB default,
/// so plain <c>dotnet test</c> works locally with no setup. On macOS/Linux
/// (or if you'd rather not use LocalDB), run a SQL Server container and
/// set that environment variable yourself — see CLAUDE.md.
/// </remarks>
public class BookingSqlServerFixture : IAsyncLifetime
{
    private const string DefaultLocalConnectionString =
        @"Server=(localdb)\mssqllocaldb;Trusted_Connection=True;TrustServerCertificate=True";

    private string _databaseName = string.Empty;
    private string _masterConnectionString = string.Empty;

    /// <summary>The <see cref="BookingsApiFactory"/> backed by this fixture's database.</summary>
    public BookingsApiFactory Factory { get; private set; } = null!;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        var baseConnectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? DefaultLocalConnectionString;

        var builder = new SqlConnectionStringBuilder(baseConnectionString) { InitialCatalog = "master" };
        _masterConnectionString = builder.ConnectionString;
        _databaseName = $"MRBS_Test_{Guid.NewGuid():N}";

        await using (var connection = await OpenMasterConnectionWithRetryAsync())
        {
            await ExecuteAsync(connection, $"CREATE DATABASE [{_databaseName}]");

            // Immediately after creation, before anything else has a
            // connection open to this database — ALTER DATABASE ... SET
            // READ_COMMITTED_SNAPSHOT blocks waiting for other sessions to
            // drain otherwise, and there's no reason to risk that here.
            await ExecuteAsync(connection, $"ALTER DATABASE [{_databaseName}] SET READ_COMMITTED_SNAPSHOT ON");
        }

        builder.InitialCatalog = _databaseName;
        Factory = new BookingsApiFactory(builder.ConnectionString);

        // Force the host to build now (real SQL Server migration + seeding
        // runs as part of that), rather than lazily inside the first test.
        _ = Factory.Services;
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        Factory.Dispose();

        // Release pooled connections to the test database before dropping
        // it — otherwise DROP DATABASE can fail with "database in use."
        SqlConnection.ClearAllPools();

        await using var connection = new SqlConnection(_masterConnectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
        await ExecuteAsync(connection, $"DROP DATABASE [{_databaseName}]");
    }

    private async Task<SqlConnection> OpenMasterConnectionWithRetryAsync()
    {
        const int maxAttempts = 10;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var connection = new SqlConnection(_masterConnectionString);
            try
            {
                await connection.OpenAsync();
                return connection;
            }
            catch (SqlException) when (attempt < maxAttempts)
            {
                await connection.DisposeAsync();
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }

        // Last attempt: let the exception surface directly.
        var finalConnection = new SqlConnection(_masterConnectionString);
        await finalConnection.OpenAsync();
        return finalConnection;
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
