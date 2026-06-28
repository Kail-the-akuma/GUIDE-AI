using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Guide.Core.Interfaces;
using Guide.Core.Models;

namespace Guide.Validation
{
    #region Dependencies & Refs

    #endregion

    public class WorkflowEngine : IWorkflowEngine
    {
        #region Fields

        private readonly IEnumerable<ISemanticParser> _parsers;
        private readonly IEnumerable<ILanguageValidator> _validators;
        private readonly IKnowledgeStore _store;
        private readonly IEngineeringMemory _memory;
        private readonly ILlmService _llmService;

        #endregion

        #region Initialization / Constructor

        public WorkflowEngine(
            IEnumerable<ISemanticParser> parsers,
            IEnumerable<ILanguageValidator> validators,
            IKnowledgeStore store,
            IEngineeringMemory memory,
            ILlmService llmService)
        {
            _parsers = parsers ?? throw new ArgumentNullException(nameof(parsers));
            _validators = validators ?? throw new ArgumentNullException(nameof(validators));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _memory = memory ?? throw new ArgumentNullException(nameof(memory));
            _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
        }

        #endregion

        #region Core Execution Workflow

        public async Task<WorkflowResult> RunWorkflowAsync(string taskDescription, string targetFilePath, CancellationToken ct)
        {
            string repoRoot = LocateRepositoryRoot(targetFilePath);
            string solutionPath = FindSolutionPath(repoRoot);

            string ext = Path.GetExtension(targetFilePath);
            ISemanticParser? parser = _parsers.FirstOrDefault(p => p.CanParse(ext));
            CodeContext? context = null;

            if (parser != null)
            {
                context = await parser.GetContextAsync(targetFilePath);
            }

            int latestVersion = await _store.GetLatestGraphVersionAsync();
            if (latestVersion > 0)
            {
                ExtractedKnowledge snapshot = await _store.GetSnapshotAsync(latestVersion);
                List<RichNode> activeRules = snapshot.Nodes
                    .Where(n => n.NodeType == "ADR" && n.Properties.TryGetValue("AppliesTo", out string? appliesTo) && 
                                !string.IsNullOrWhiteSpace(appliesTo) &&
                                (appliesTo.Contains(Path.GetFileNameWithoutExtension(targetFilePath), StringComparison.OrdinalIgnoreCase) ||
                                 (context != null && context.Classes.Any(c => appliesTo.Contains(c, StringComparison.OrdinalIgnoreCase)))))
                    .ToList();
            }

            string language = DetectLanguage(ext);
            ILanguageValidator? validator = _validators.FirstOrDefault(v => v.Language.Equals(language, StringComparison.OrdinalIgnoreCase));
            if (validator == null)
            {
                return new WorkflowResult
                {
                    IsSuccess = false,
                    Errors = new List<string> { $"No language validator found for language '{language}' (extension '{ext}')" }
                };
            }

            ValidationResult validationResult = await validator.ValidateAsync(solutionPath, runTests: false);
            if (validationResult.IsSuccess)
            {
                return new WorkflowResult
                {
                    IsSuccess = true,
                    Errors = new List<string>()
                };
            }

            CommandLineRunner runner = new CommandLineRunner();
            AutoHealer healer = new AutoHealer(runner, _store, _llmService, _memory);
            HealingResult healingResult = await healer.HealAsync(solutionPath, repoRoot, targetFilePath, validationResult.Errors, ct);

            return new WorkflowResult
            {
                IsSuccess = healingResult.IsSuccess,
                Errors = healingResult.Errors,
                HealingIterations = healingResult.Iterations
            };
        }

        #endregion

        #region Helper Methods

        private string LocateRepositoryRoot(string targetFilePath)
        {
            DirectoryInfo? dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(targetFilePath)) ?? ".");
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            return Path.GetDirectoryName(Path.GetFullPath(targetFilePath)) ?? ".";
        }

        private string FindSolutionPath(string repoRoot)
        {
            string[] slnFiles = Directory.GetFiles(repoRoot, "*.sln", SearchOption.TopDirectoryOnly);
            if (slnFiles.Length > 0)
            {
                return slnFiles[0];
            }

            slnFiles = Directory.GetFiles(repoRoot, "*.sln", SearchOption.AllDirectories);
            if (slnFiles.Length > 0)
            {
                return slnFiles[0];
            }

            string[] csprojFiles = Directory.GetFiles(repoRoot, "*.csproj", SearchOption.AllDirectories);
            if (csprojFiles.Length > 0)
            {
                return csprojFiles[0];
            }

            return repoRoot;
        }

        private string DetectLanguage(string extension)
        {
            if (string.IsNullOrEmpty(extension))
            {
                return string.Empty;
            }

            if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase))
            {
                return "CSharp";
            }

            if (extension.Equals(".ts", StringComparison.OrdinalIgnoreCase) || 
                extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase) || 
                extension.Equals(".js", StringComparison.OrdinalIgnoreCase) || 
                extension.Equals(".jsx", StringComparison.OrdinalIgnoreCase))
            {
                return "TypeScript";
            }

            return string.Empty;
        }

        #endregion
    }
}
