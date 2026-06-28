using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Guide.Core.Interfaces;

namespace Guide.Memory;

public class EngineeringMemoryStore : IEngineeringMemory
{
    private readonly string _connectionString;
    private readonly string _sqliteConnectionString;

    public EngineeringMemoryStore(string connectionString = "Data Source=.guide/engineering_memory.db;Busy Timeout=5000;Cache=Shared;Mode=ReadWriteCreate;")
    {
        _connectionString = connectionString;
        _sqliteConnectionString = connectionString
            .Replace("Busy Timeout=5000;", "", StringComparison.OrdinalIgnoreCase)
            .Replace("Busy Timeout=5000", "", StringComparison.OrdinalIgnoreCase);

        EnsureDirectoryExists();
        InitializeDatabase();
    }

    private void EnsureDirectoryExists()
    {
        try
        {
            SqliteConnectionStringBuilder builder = new SqliteConnectionStringBuilder(_sqliteConnectionString);
            string dataSource = builder.DataSource;
            if (!string.IsNullOrEmpty(dataSource) && dataSource != ":memory:")
            {
                string? dir = Path.GetDirectoryName(dataSource);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }
        }
        catch
        {
            try
            {
                if (!Directory.Exists(".guide"))
                {
                    Directory.CreateDirectory(".guide");
                }
            }
            catch
            {
                /* Ignore */
            }
        }
    }

    private void InitializeDatabase()
    {
        using SqliteConnection connection = new SqliteConnection(_sqliteConnectionString);
        connection.Open();

        using (SqliteCommand pragmaCmd = connection.CreateCommand())
        {
            pragmaCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
            pragmaCmd.ExecuteNonQuery();
        }

        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Corrections (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ErrorCode TEXT,
                ErrorLog TEXT,
                OriginalSnippet TEXT,
                PatchedSnippet TEXT,
                IsSuccess INTEGER,
                Timestamp TEXT,
                FileExtension TEXT
            );
        ";
        cmd.ExecuteNonQuery();

        try
        {
            using SqliteCommand alterCmd = connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE Corrections ADD COLUMN FileExtension TEXT;";
            alterCmd.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            /* Column might already exist */
        }
    }

    private async Task<SqliteConnection> GetOpenConnectionAsync()
    {
        SqliteConnection connection = new SqliteConnection(_sqliteConnectionString);
        await connection.OpenAsync();
        using (SqliteCommand cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
            await cmd.ExecuteNonQueryAsync();
        }
        return connection;
    }

    public async Task RecordCorrectionAsync(string errorCode, string errorLog, string originalSnippet, string patchedSnippet, bool isSuccess, string? fileExtension = null)
    {
        using SqliteConnection connection = await GetOpenConnectionAsync();
        using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
        try
        {
            using SqliteCommand cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                INSERT INTO Corrections (ErrorCode, ErrorLog, OriginalSnippet, PatchedSnippet, IsSuccess, Timestamp, FileExtension)
                VALUES (@ErrorCode, @ErrorLog, @OriginalSnippet, @PatchedSnippet, @IsSuccess, @Timestamp, @FileExtension)";

            cmd.Parameters.AddWithValue("@ErrorCode", errorCode ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@ErrorLog", errorLog ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@OriginalSnippet", originalSnippet ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@PatchedSnippet", patchedSnippet ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@IsSuccess", isSuccess ? 1 : 0);
            cmd.Parameters.AddWithValue("@Timestamp", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("@FileExtension", fileExtension ?? (object)DBNull.Value);

            await cmd.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static List<string> GetPreFilterWords(string errorLog)
    {
        if (string.IsNullOrEmpty(errorLog))
        {
            return new List<string>();
        }

        HashSet<string> words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        System.Text.StringBuilder currentWord = new System.Text.StringBuilder();

        for (int i = 0; i < errorLog.Length; i++)
        {
            char c = errorLog[i];
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c))
            {
                if (currentWord.Length >= 4)
                {
                    words.Add(currentWord.ToString());
                }
                currentWord.Clear();
            }
            else
            {
                currentWord.Append(c);
            }
        }

        if (currentWord.Length >= 4)
        {
            words.Add(currentWord.ToString());
        }

        return words
            .OrderByDescending(w => w.Length)
            .ThenBy(w => w, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
    }

    public async Task<IEnumerable<MemoryMatch>> FindSimilarCorrectionsAsync(string errorCode, string errorLog, double similarityThreshold = 0.7, string? fileExtension = null)
    {
        using SqliteConnection connection = await GetOpenConnectionAsync();
        List<(string OriginalSnippet, string PatchedSnippet, double Score, string? FileExtension)> matches = new List<(string OriginalSnippet, string PatchedSnippet, double Score, string? FileExtension)>();

        int count = 0;
        using (SqliteCommand countCmd = connection.CreateCommand())
        {
            countCmd.CommandText = "SELECT COUNT(*) FROM Corrections WHERE IsSuccess = 1 AND ErrorCode = @ErrorCode";
            countCmd.Parameters.AddWithValue("@ErrorCode", errorCode ?? (object)DBNull.Value);
            count = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
        }

        using (SqliteCommand cmd = connection.CreateCommand())
        {
            cmd.Parameters.AddWithValue("@ErrorCode", errorCode ?? (object)DBNull.Value);

            bool applyPreFiltering = count > 50;
            List<string> words = applyPreFiltering ? GetPreFilterWords(errorLog) : new List<string>();

            if (applyPreFiltering && words.Count > 0)
            {
                List<string> likeConditions = new List<string>();
                for (int i = 0; i < words.Count; i++)
                {
                    likeConditions.Add($"ErrorLog LIKE @W{i}");
                }
                string likeClause = " AND (" + string.Join(" OR ", likeConditions) + ")";

                if (string.IsNullOrEmpty(fileExtension))
                {
                    cmd.CommandText = $@"
                        SELECT ErrorLog, OriginalSnippet, PatchedSnippet, FileExtension
                        FROM Corrections
                        WHERE IsSuccess = 1 AND ErrorCode = @ErrorCode{likeClause}";
                }
                else
                {
                    cmd.CommandText = $@"
                        SELECT ErrorLog, OriginalSnippet, PatchedSnippet, FileExtension
                        FROM Corrections
                        WHERE IsSuccess = 1 AND ErrorCode = @ErrorCode AND (FileExtension = @FileExtension OR FileExtension IS NULL){likeClause}";
                    cmd.Parameters.AddWithValue("@FileExtension", fileExtension);
                }

                for (int i = 0; i < words.Count; i++)
                {
                    cmd.Parameters.AddWithValue($"@W{i}", "%" + words[i] + "%");
                }
            }
            else
            {
                if (string.IsNullOrEmpty(fileExtension))
                {
                    cmd.CommandText = @"
                        SELECT ErrorLog, OriginalSnippet, PatchedSnippet, FileExtension
                        FROM Corrections
                        WHERE IsSuccess = 1 AND ErrorCode = @ErrorCode";
                }
                else
                {
                    cmd.CommandText = @"
                        SELECT ErrorLog, OriginalSnippet, PatchedSnippet, FileExtension
                        FROM Corrections
                        WHERE IsSuccess = 1 AND ErrorCode = @ErrorCode AND (FileExtension = @FileExtension OR FileExtension IS NULL)";
                    cmd.Parameters.AddWithValue("@FileExtension", fileExtension);
                }
            }

            using SqliteDataReader reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string dbErrorLog = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                string originalSnippet = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                string patchedSnippet = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                string? dbFileExt = reader.IsDBNull(3) ? null : reader.GetString(3);

                double score = JaccardSimilarity.Compute(errorLog, dbErrorLog);
                if (score >= similarityThreshold)
                {
                    matches.Add((originalSnippet, patchedSnippet, score, dbFileExt));
                }
            }
        }

        return matches
            .OrderByDescending(m => m.Score)
            .Select(m => new MemoryMatch(m.OriginalSnippet, m.PatchedSnippet, m.Score, m.FileExtension))
            .ToList();
    }
}
