using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Guide.Semantic
{
    public class FallbackAstParser
    {
        private readonly SqliteKnowledgeStore _store;

        public FallbackAstParser(SqliteKnowledgeStore store)
        {
            _store = store;
        }

        private static string? GetGitRepoRoot(string path)
        {
            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "rev-parse --show-toplevel",
                    WorkingDirectory = path,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process != null)
                {
                    string output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();
                    if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                    {
                        return Path.GetFullPath(output);
                    }
                }
            }
            catch
            {
                // Fallback to directory walk
            }

            var dir = new DirectoryInfo(path);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }

            return null;
        }

        public async Task<IEnumerable<SyntaxTree>> ParseDirectoryAsync(string directoryPath, CancellationToken ct)
        {
            var parsedTrees = new List<SyntaxTree>();
            if (!Directory.Exists(directoryPath))
            {
                return parsedTrees;
            }

            var gitRepoRoot = GetGitRepoRoot(directoryPath);
            var files = Directory.EnumerateFiles(directoryPath, "*.cs", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var absoluteFile = Path.GetFullPath(file);
                var storedPath = gitRepoRoot != null
                    ? Path.GetRelativePath(gitRepoRoot, absoluteFile).Replace("\\", "/")
                    : absoluteFile;

                var fileInfo = new FileInfo(absoluteFile);
                long lastWriteTime = fileInfo.LastWriteTimeUtc.Ticks;
                string currentHash;
                try
                {
                    currentHash = CalculateMd5(absoluteFile);
                }
                catch (IOException)
                {
                    // Skip files that are locked or can't be read
                    continue;
                }

                // Check database to see if we can skip this file
                var cachedHash = await _store.GetFileHashAsync(storedPath, lastWriteTime);
                if (cachedHash == currentHash)
                {
                    continue;
                }

                // File has changed or is new, parse it
                string content;
                try
                {
                    content = await File.ReadAllTextAsync(absoluteFile, ct);
                }
                catch (IOException)
                {
                    continue;
                }

                var syntaxTree = CSharpSyntaxTree.ParseText(content, path: storedPath, cancellationToken: ct);
                parsedTrees.Add(syntaxTree);

                // Update database with the new hash
                await _store.SaveFileHashAsync(storedPath, lastWriteTime, currentHash);
            }

            return parsedTrees;
        }

        private static string CalculateMd5(string filePath)
        {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(filePath);
            var hashBytes = md5.ComputeHash(stream);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }
    }
}
