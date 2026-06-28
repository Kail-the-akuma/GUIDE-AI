using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Guide.Core.Interfaces;
using Guide.Core.Models;

namespace Guide.Validation;

public class HealingResult
{
    public bool IsSuccess { get; set; }
    public int Iterations { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class AutoHealer
{
    private readonly ICommandLineRunner _runner;
    private readonly IKnowledgeStore _store;
    private readonly ILlmService _llmService;
    private readonly IEngineeringMemory? _memory;

    public AutoHealer(ICommandLineRunner runner, IKnowledgeStore store, ILlmService llmService, IEngineeringMemory? memory = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
        _memory = memory;
    }

    private async Task<ValidationResult> RunValidationAsync(string solutionPath, string repoRoot, string targetFilePath, CancellationToken ct)
    {
        if (targetFilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
            targetFilePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
        {
            ParallelValidator validator = new ParallelValidator(_runner);
            return await validator.ValidateProjectAsync(solutionPath, ct);
        }

        LanguageRegistry registry = LanguageRegistry.Detect(repoRoot, _runner);
        ValidationResult validationResult = new ValidationResult { IsSuccess = true, Errors = new List<string>() };
        foreach (ILanguageValidator validator in registry.Validators)
        {
            ValidationResult res = await validator.ValidateAsync(solutionPath, runTests: false);
            if (!res.IsSuccess)
            {
                validationResult.IsSuccess = false;
                validationResult.Errors.AddRange(res.Errors);
            }
        }
        return validationResult;
    }

    private static (string ErrorCode, string ErrorLog) ExtractErrorCodeAndLog(IEnumerable<string> errors)
    {
        if (errors == null || !errors.Any())
        {
            return ("UNKNOWN", "");
        }

        foreach (string err in errors)
        {
            if (string.IsNullOrWhiteSpace(err))
            {
                continue;
            }

            if (err.Contains("CSS syntax error", StringComparison.OrdinalIgnoreCase))
            {
                return ("CSS_SYNTAX", CompressError(err));
            }
            if (err.Contains("HTML syntax error", StringComparison.OrdinalIgnoreCase))
            {
                return ("HTML_SYNTAX", CompressError(err));
            }

            Match tsMatch = Regex.Match(err, @"\berror\s+(TS\d+)\b", RegexOptions.IgnoreCase);
            if (tsMatch.Success)
            {
                return (tsMatch.Groups[1].Value.ToUpperInvariant(), CompressError(err));
            }

            Match csMatch = Regex.Match(err, @"\berror\s+([A-Za-z]+\d+)\b", RegexOptions.IgnoreCase);
            if (csMatch.Success)
            {
                return (csMatch.Groups[1].Value.ToUpperInvariant(), CompressError(err));
            }

            Match eslintMatch = Regex.Match(err, @"\[([^\]]+)\]\s*$");
            if (eslintMatch.Success)
            {
                return (eslintMatch.Groups[1].Value, CompressError(err));
            }

            Match eslintWordMatch = Regex.Match(err, @"\s+([a-zA-Z0-9\-\/]+)\s*$");
            if (eslintWordMatch.Success && eslintWordMatch.Groups[1].Value.Contains('-'))
            {
                return (eslintWordMatch.Groups[1].Value, CompressError(err));
            }
        }

        string firstErr = errors.First();
        string log = CompressError(firstErr);
        Match matchCode = Regex.Match(log, @"\b([A-Z]{2,}\d+)\b");
        if (matchCode.Success)
        {
            return (matchCode.Groups[1].Value, log);
        }

        return ("GENERIC", log);
    }

    public async Task<HealingResult> HealAsync(string solutionPath, string repoRoot, string targetFilePath, IEnumerable<string> errors, CancellationToken ct)
    {
        (string extractedCode, string extractedLog) = ExtractErrorCodeAndLog(errors);
        string errorCode = extractedCode;
        string errorLog = extractedLog;

        if (_memory != null && !string.IsNullOrEmpty(errorCode) && !string.IsNullOrEmpty(errorLog))
        {
            string fileExt = Path.GetExtension(targetFilePath);
            double threshold = 0.75;
            bool isFrontend = fileExt.Equals(".js", StringComparison.OrdinalIgnoreCase) ||
                              fileExt.Equals(".jsx", StringComparison.OrdinalIgnoreCase) ||
                              fileExt.Equals(".ts", StringComparison.OrdinalIgnoreCase) ||
                              fileExt.Equals(".tsx", StringComparison.OrdinalIgnoreCase) ||
                              fileExt.Equals(".css", StringComparison.OrdinalIgnoreCase) ||
                              fileExt.Equals(".html", StringComparison.OrdinalIgnoreCase);

            if (isFrontend)
            {
                threshold = 0.80; // Score > 80% for frontend validation
            }

            IEnumerable<MemoryMatch> matches = await _memory.FindSimilarCorrectionsAsync(errorCode, errorLog, threshold, fileExt);
            MemoryMatch? bestMatch = matches?.FirstOrDefault();
            if (bestMatch != null)
            {
                Console.WriteLine($"[Engineering Memory] Found a similar past correction (Score: {bestMatch.SimilarityScore:P0}). Applying cached patch...");
                string? originalFileContent = null;
                try
                {
                    originalFileContent = await File.ReadAllTextAsync(targetFilePath, ct);
                    string patchedContent = PatchApplier.ApplyPatches(originalFileContent, bestMatch.PatchedSnippet);
                    await File.WriteAllTextAsync(targetFilePath, patchedContent, ct);

                    // Re-compile and re-validate
                    ValidationResult validationResult = await RunValidationAsync(solutionPath, repoRoot, targetFilePath, ct);

                    if (validationResult.IsSuccess)
                    {
                        return new HealingResult
                        {
                            IsSuccess = true,
                            Iterations = 0,
                            Errors = new List<string>()
                        };
                    }
                    else
                    {
                        // Restore original file content
                        await File.WriteAllTextAsync(targetFilePath, originalFileContent, ct);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Engineering Memory] Failed to apply cached patch: {ex.Message}");
                    if (originalFileContent != null)
                    {
                        try
                        {
                            await File.WriteAllTextAsync(targetFilePath, originalFileContent, ct);
                        }
                        catch
                        {
                            /* Ignore */
                        }
                    }
                }
            }
        }

        string fileName = Path.GetFileName(targetFilePath);
        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(targetFilePath);

        List<string> currentErrors = errors?
            .Select(CompressError)
            .Where(err => err.Contains(fileName, StringComparison.OrdinalIgnoreCase) ||
                          err.Contains(fileNameWithoutExt, StringComparison.OrdinalIgnoreCase))
            .ToList() ?? new List<string>();

        if (currentErrors.Count == 0 && errors != null && errors.Any())
        {
            currentErrors = errors.Select(CompressError).ToList();
        }

        for (int i = 1; i <= 5; i++)
        {
            if (ct.IsCancellationRequested)
            {
                return new HealingResult
                {
                    IsSuccess = false,
                    Iterations = i - 1,
                    Errors = new List<string> { "Task was cancelled." }
                };
            }

            string fileContent;
            try
            {
                fileContent = await File.ReadAllTextAsync(targetFilePath, ct);
            }
            catch (Exception ex)
            {
                return new HealingResult
                {
                    IsSuccess = false,
                    Iterations = i - 1,
                    Errors = new List<string> { $"Failed to read target file: {ex.Message}" }
                };
            }

            string promptFileContent = fileContent;
            try
            {
                string fileExt = Path.GetExtension(targetFilePath);
                LanguageRegistry registry = LanguageRegistry.Detect(repoRoot, _runner);
                ICommentStripper? stripper = registry.Strippers.FirstOrDefault(s => s.CanStrip(fileExt));
                if (stripper != null)
                {
                    promptFileContent = stripper.StripComments(fileContent);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AutoHealer] Failed to strip comments: {ex.Message}");
            }

            string prompt = $@"INSTRUCTIONS & SYSTEM GUIDANCE:;
You are an automated code repair agent. Your task is to resolve compilation, syntax, or architectural validation errors in a source file.
Please analyze the file content and the validation errors provided below, then generate a code patch.

RULE BOUNDARIES & GENERAL GUIDANCE:
1. Provide a patch using the SEARCH/REPLACE block format.
2. The SEARCH block must match the existing code in the file exactly, character-for-character, including whitespace, tabs, and newlines.
3. The REPLACE block must contain the corrected code.
4. Keep modifications minimal and focused on resolving the validation errors. Do not rewrite unrelated code.
5. Ensure the resulting code is syntactically valid and respects the architecture boundaries.

SEARCH/REPLACE BLOCK FORMAT DESCRIPTION:
<<<<<<< SEARCH
[exact code from the file]
=======
[replacement code]
>>>>>>> REPLACE

DYNAMIC PARAMETERS:
Target File: {targetFilePath}
Validation Errors:
{string.Join(Environment.NewLine, currentErrors)}
File Content:
{promptFileContent}
";

            string patchText = await _llmService.GeneratePatchAsync(prompt, ct);
            if (string.IsNullOrEmpty(patchText))
            {
                currentErrors.Add(CompressError("LLM service returned an empty patch."));
                return new HealingResult
                {
                    IsSuccess = false,
                    Iterations = i,
                    Errors = currentErrors
                };
            }

            string patchedContent;
            try
            {
                patchedContent = PatchApplier.ApplyPatches(promptFileContent, patchText);
            }
            catch (Exception ex)
            {
                currentErrors.Add(CompressError($"Failed to apply patch: {ex.Message}"));
                return new HealingResult
                {
                    IsSuccess = false,
                    Iterations = i,
                    Errors = currentErrors
                };
            }

            try
            {
                await File.WriteAllTextAsync(targetFilePath, patchedContent, ct);
            }
            catch (Exception ex)
            {
                currentErrors.Add(CompressError($"Failed to write patched content to file: {ex.Message}"));
                return new HealingResult
                {
                    IsSuccess = false,
                    Iterations = i,
                    Errors = currentErrors
                };
            }

            // Fast In-Memory Syntax Gate for C# only
            if (targetFilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                targetFilePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(patchedContent, cancellationToken: ct);
                IEnumerable<Diagnostic> diagnostics = syntaxTree.GetDiagnostics(ct);
                List<Diagnostic> syntaxErrors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
                if (syntaxErrors.Any())
                {
                    currentErrors = syntaxErrors.Select(d =>
                    {
                        FileLinePositionSpan lineSpan = d.Location.GetLineSpan();
                        int line = lineSpan.StartLinePosition.Line + 1;
                        int col = lineSpan.StartLinePosition.Character + 1;
                        string formatted = $"{fileName}({line},{col}): error {d.Id}: {d.GetMessage()}";
                        return CompressError(formatted);
                    }).ToList();

                    continue;
                }
            }

            // Re-compile and re-validate using dynamically detected validators
            ValidationResult validationResult = await RunValidationAsync(solutionPath, repoRoot, targetFilePath, ct);

            if (validationResult.IsSuccess)
            {
                if (_memory != null && !string.IsNullOrEmpty(errorCode) && !string.IsNullOrEmpty(errorLog))
                {
                    string originalSnippet = ExtractOriginalSnippet(patchText);
                    string fileExt = Path.GetExtension(targetFilePath);
                    await _memory.RecordCorrectionAsync(errorCode, errorLog, originalSnippet, patchText, true, fileExt);
                }

                return new HealingResult
                {
                    IsSuccess = true,
                    Iterations = i,
                    Errors = new List<string>()
                };
            }
            else
            {
                currentErrors = validationResult.Errors
                    .Select(CompressError)
                    .Where(err => err.Contains(fileName, StringComparison.OrdinalIgnoreCase) ||
                                  err.Contains(fileNameWithoutExt, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (currentErrors.Count == 0 && validationResult.Errors.Any())
                {
                    currentErrors = validationResult.Errors.Select(CompressError).ToList();
                }
            }
        }

        return new HealingResult
        {
            IsSuccess = false,
            Iterations = 5,
            Errors = currentErrors
        };
    }

    private static string ExtractOriginalSnippet(string patchText)
    {
        if (string.IsNullOrEmpty(patchText))
        {
            return string.Empty;
        }
        string[] lines = patchText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        List<string> searchLines = new List<string>();
        bool inSearch = false;
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("<<<<<<< SEARCH"))
            {
                inSearch = true;
            }
            else if (trimmed.StartsWith("======="))
            {
                inSearch = false;
            }
            else if (trimmed.StartsWith(">>>>>>> REPLACE"))
            {
                inSearch = false;
            }
            else if (inSearch)
            {
                searchLines.Add(line);
            }
        }
        return string.Join(Environment.NewLine, searchLines);
    }

    private static string CompressError(string error)
    {
        if (string.IsNullOrEmpty(error))
        {
            return error;
        }

        // Cleans up and sanitizes the MSBuild compile errors by removing project suffixes (like '[C:\...\project.csproj]') using Regular Expressions.
        error = Regex.Replace(error, @"\s*\[[^\]]+\.csproj\]", "");

        // Replaces absolute system file paths with only the file name (basename).
        error = Regex.Replace(error, @"(?:\b[a-zA-Z]:\\[^\(\):]+|(?<=^|\s)/[^\(\):]+)", m =>
        {
            string pathStr = m.Value.Trim();
            int lastSlash = pathStr.LastIndexOfAny(new[] { '/', '\\' });
            string fileName = lastSlash >= 0 ? pathStr.Substring(lastSlash + 1) : pathStr;
            string trailingSpaces = m.Value.Substring(pathStr.Length);
            return fileName + trailingSpaces;
        });

        return error;
    }
}
