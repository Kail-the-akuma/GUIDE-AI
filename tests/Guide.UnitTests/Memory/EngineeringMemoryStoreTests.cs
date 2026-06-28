using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Guide.Memory;

namespace Guide.UnitTests.Memory;

public class EngineeringMemoryStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly EngineeringMemoryStore _store;

    public EngineeringMemoryStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"pea_ia_test_memory_{Guid.NewGuid()}.db");
        _connectionString = $"Data Source={_dbPath};Busy Timeout=5000;Cache=Shared;Mode=ReadWriteCreate;";
        _store = new EngineeringMemoryStore(_connectionString);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch
        {
            // Ignore cleanup errors in test run
        }
    }

    [Fact]
    public async Task RecordAndFindSimilarCorrections_WorksCorrectly()
    {
        // 1. Record correction
        await _store.RecordCorrectionAsync("ERR-100", "Object reference not set to an instance of an object.", "foo.Bar()", "foo?.Bar()", true);
        await _store.RecordCorrectionAsync("ERR-100", "Another unrelated error message.", "x = y", "x = y ?? z", true);
        await _store.RecordCorrectionAsync("ERR-100", "Object reference not set to an instance of an object.", "different", "different", false); // IsSuccess = false
        await _store.RecordCorrectionAsync("ERR-200", "Object reference not set to an instance of an object.", "unrelated", "unrelated", true); // Different error code

        // 2. Find similar
        var matches = (await _store.FindSimilarCorrectionsAsync("ERR-100", "Object reference not set to an instance of an object.", 0.7)).ToList();

        // 3. Assert
        Assert.Single(matches);
        var match = matches[0];
        Assert.Equal("foo.Bar()", match.OriginalSnippet);
        Assert.Equal("foo?.Bar()", match.PatchedSnippet);
        Assert.Equal(1.0, match.SimilarityScore);
    }

    [Fact]
    public async Task RecordAndFindSimilarCorrections_WithFileExtension_WorksCorrectly()
    {
        // 1. Record corrections with file extensions
        await _store.RecordCorrectionAsync("ERR-300", "Linter warning: unexpected token", "const a = 1", "const a = 1;", true, ".js");
        await _store.RecordCorrectionAsync("ERR-300", "Linter warning: unexpected token", "let b = 2", "let b = 2;", true, ".ts");

        // 2. Find similar specifying file extension
        var jsMatches = (await _store.FindSimilarCorrectionsAsync("ERR-300", "Linter warning: unexpected token", 0.7, ".js")).ToList();
        var tsMatches = (await _store.FindSimilarCorrectionsAsync("ERR-300", "Linter warning: unexpected token", 0.7, ".ts")).ToList();

        // 3. Assert
        Assert.Single(jsMatches);
        Assert.Equal(".js", jsMatches[0].FileExtension);
        Assert.Equal("const a = 1", jsMatches[0].OriginalSnippet);

        Assert.Single(tsMatches);
        Assert.Equal(".ts", tsMatches[0].FileExtension);
        Assert.Equal("let b = 2", tsMatches[0].OriginalSnippet);
    }

    [Fact]
    public async Task FindSimilarCorrections_WithMoreThan50Corrections_AppliesPreFilteringSuccessfully()
    {
        // 1. Record 51 corrections with some random logs
        for (int i = 0; i < 51; i++)
        {
            await _store.RecordCorrectionAsync("ERR-500", $"This is generic error log number {i} containing some boilerplate text.", $"snippet{i}", $"patched{i}", true);
        }

        // 2. Record 1 target correction with a very specific, long word
        string targetLog = "Crucial issue with PreFilteringOptimizedVerification in database memory store.";
        await _store.RecordCorrectionAsync("ERR-500", targetLog, "targetOriginal", "targetPatched", true);

        // 3. Search using an error log that contains that specific word
        var matches = (await _store.FindSimilarCorrectionsAsync("ERR-500", "PreFilteringOptimizedVerification exception occurred.", 0.2)).ToList();

        // 4. Assert
        Assert.Single(matches);
        Assert.Equal("targetOriginal", matches[0].OriginalSnippet);
        Assert.Equal("targetPatched", matches[0].PatchedSnippet);
    }
}
