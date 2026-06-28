using System.Collections.Generic;
using System.Threading.Tasks;

namespace Guide.Core.Interfaces;

public interface IEngineeringMemory
{
    Task RecordCorrectionAsync(string errorCode, string errorLog, string originalSnippet, string patchedSnippet, bool isSuccess, string? fileExtension = null);
    Task<IEnumerable<MemoryMatch>> FindSimilarCorrectionsAsync(string errorCode, string errorLog, double similarityThreshold = 0.7, string? fileExtension = null);
}

public record MemoryMatch(
    string OriginalSnippet,
    string PatchedSnippet,
    double SimilarityScore,
    string? FileExtension = null
);
