using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Guide.Benchmarks;

public record RunResult(
    string TaskId,
    bool Success,
    double DurationSeconds,
    int InputTokens,
    int OutputTokens,
    int HealingCycles,
    int ArchitectureViolations
);

public class BenchmarkDbStore
{
    private readonly string _connectionString;
    private readonly string _sqliteConnectionString;

    public BenchmarkDbStore(string dbPath = ".guide/benchmark_results.db")
    {
        // Enforce busy_timeout=5000 in connection string
        _connectionString = $"Data Source={dbPath};Cache=Shared;Mode=ReadWriteCreate;Busy Timeout=5000;";
        _sqliteConnectionString = _connectionString
            .Replace("Busy Timeout=5000;", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Busy Timeout=5000", "", StringComparison.OrdinalIgnoreCase);
    }

    public async Task InitializeDatabaseAsync()
    {
        var dbDir = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".guide/benchmark_results.db")));
        if (dbDir != null && !Directory.Exists(dbDir))
        {
            Directory.CreateDirectory(dbDir);
        }

        using var connection = new SqliteConnection(_sqliteConnectionString);
        await connection.OpenAsync();

        using (var pragmaCmd = new SqliteCommand("PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;", connection))
        {
            await pragmaCmd.ExecuteNonQueryAsync();
        }

        var createTableQuery = @"
            CREATE TABLE IF NOT EXISTS BenchmarkRuns (
                RunId TEXT PRIMARY KEY,
                Timestamp TEXT NOT NULL,
                GroupType TEXT NOT NULL,
                TaskId TEXT NOT NULL,
                Success INTEGER NOT NULL,
                DurationSeconds REAL NOT NULL,
                InputTokens INTEGER NOT NULL,
                OutputTokens INTEGER NOT NULL,
                CostUsd REAL NOT NULL,
                HealingCycles INTEGER NOT NULL,
                ArchitectureViolations INTEGER NOT NULL
            );";
        using var command = new SqliteCommand(createTableQuery, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task SaveResultAsync(RunResult result, string groupType, double costUsd)
    {
        using var connection = new SqliteConnection(_sqliteConnectionString);
        await connection.OpenAsync();

        using (var pragmaCmd = new SqliteCommand("PRAGMA busy_timeout=5000;", connection))
        {
            await pragmaCmd.ExecuteNonQueryAsync();
        }

        using var transaction = connection.BeginTransaction();
        try
        {
            var insertQuery = @"
                INSERT INTO BenchmarkRuns (RunId, Timestamp, GroupType, TaskId, Success, DurationSeconds, InputTokens, OutputTokens, CostUsd, HealingCycles, ArchitectureViolations)
                VALUES (@RunId, @Timestamp, @GroupType, @TaskId, @Success, @DurationSeconds, @InputTokens, @OutputTokens, @CostUsd, @HealingCycles, @ArchitectureViolations);";

            using var command = new SqliteCommand(insertQuery, connection, transaction);
            command.Parameters.AddWithValue("@RunId", Guid.NewGuid().ToString());
            command.Parameters.AddWithValue("@Timestamp", DateTime.UtcNow.ToString("o"));
            command.Parameters.AddWithValue("@GroupType", groupType);
            command.Parameters.AddWithValue("@TaskId", result.TaskId);
            command.Parameters.AddWithValue("@Success", result.Success ? 1 : 0);
            command.Parameters.AddWithValue("@DurationSeconds", result.DurationSeconds);
            command.Parameters.AddWithValue("@InputTokens", result.InputTokens);
            command.Parameters.AddWithValue("@OutputTokens", result.OutputTokens);
            command.Parameters.AddWithValue("@CostUsd", costUsd);
            command.Parameters.AddWithValue("@HealingCycles", result.HealingCycles);
            command.Parameters.AddWithValue("@ArchitectureViolations", result.ArchitectureViolations);

            await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
