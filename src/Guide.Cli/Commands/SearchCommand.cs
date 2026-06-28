using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using Guide.Core.Interfaces;
using Guide.Core.Models;
using Guide.Knowledge;

namespace Guide.Cli.Commands
{
    public static class SearchCommand
    {
        public static async Task<int> InvokeAsync(string path, string query)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine("Error: The query cannot be empty.");
                    Console.ResetColor();
                    return 1;
                }

                // 1. Locate repository root
                string repoRoot = Path.GetFullPath(path);
                DirectoryInfo? dir = new DirectoryInfo(repoRoot);
                while (dir != null)
                {
                    if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                    {
                        repoRoot = dir.FullName;
                        break;
                    }
                    dir = dir.Parent;
                }

                string guideDir = Path.Combine(repoRoot, ".guide");
                string dbPath = Path.Combine(guideDir, "project_graph.db");

                if (!File.Exists(dbPath))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"Error: SQLite database does not exist at {dbPath}. Please run the 'init' and 'index' commands first.");
                    Console.ResetColor();
                    return 1;
                }

                // 2. Initialize knowledge store
                string connectionString = $"Data Source={dbPath};Cache=Shared;Mode=ReadWriteCreate;Busy Timeout=5000;";
                KnowledgeStore store = new KnowledgeStore(connectionString);

                // Synchronize local docs under .agents to ensure search database is up-to-date
                await store.SyncLocalDocsAsync(repoRoot);

                Console.WriteLine($"Searching for: \"{query}\"...");
                List<KnowledgeEntry> results = (await store.QueryKnowledgeAsync(query)).ToList();

                if (!results.Any())
                {
                    Console.WriteLine("No rules or knowledge entries found matching the query.");
                    return 0;
                }

                Console.WriteLine($"Found {results.Count} matching rules:\n");

                foreach (KnowledgeEntry entry in results)
                {
                    (double confidence, Dictionary<string, string> properties) = await GetNodeMetadataAsync(connectionString, entry.Title);

                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"Title:      {entry.Title}");
                    Console.ResetColor();
                    Console.WriteLine($"Category:   {entry.Category}");
                    Console.WriteLine($"Confidence: {confidence:F2}");

                    string owner = properties.TryGetValue("Owner", out string? o) ? o ?? "N/A" : "N/A";
                    string tags = properties.TryGetValue("Tags", out string? t) ? t ?? "N/A" : "N/A";
                    string priority = properties.TryGetValue("Priority", out string? p) ? p ?? "N/A" : "N/A";
                    string status = properties.TryGetValue("Status", out string? s) ? s ?? "N/A" : "N/A";
                    string appliesTo = properties.TryGetValue("AppliesTo", out string? a) ? a ?? "N/A" : "N/A";
                    string version = properties.TryGetValue("Version", out string? v) ? v ?? "N/A" : "N/A";
                    string deprecated = properties.TryGetValue("Deprecated", out string? d) ? d ?? "N/A" : "N/A";

                    Console.WriteLine($"Owner:      {owner}");
                    Console.WriteLine($"Tags:       {tags}");
                    Console.WriteLine($"Priority:   {priority}");
                    Console.WriteLine($"Status:     {status}");
                    Console.WriteLine($"AppliesTo:  {appliesTo}");
                    Console.WriteLine($"Version:    {version}");
                    Console.WriteLine($"Deprecated: {deprecated}");
                    Console.WriteLine("Content:");
                    Console.WriteLine(new string('-', 50));
                    Console.WriteLine(entry.Content.Trim());
                    Console.WriteLine(new string('-', 50));
                    Console.WriteLine();
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"Error during search: {ex.Message}");
                Console.ResetColor();
                return 1;
            }
        }

        private static async Task<(double Confidence, Dictionary<string, string> Properties)> GetNodeMetadataAsync(string connectionString, string title)
        {
            double confidence = 1.0;
            Dictionary<string, string> properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using SqliteConnection connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();

                using SqliteCommand cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT Confidence, Properties FROM Nodes WHERE Name = @Name AND NodeType = 'ADR' ORDER BY Version DESC LIMIT 1";
                cmd.Parameters.AddWithValue("@Name", title);

                using SqliteDataReader reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    if (!reader.IsDBNull(0))
                    {
                        confidence = reader.GetDouble(0);
                    }
                    if (!reader.IsDBNull(1))
                    {
                        string propsJson = reader.GetString(1);
                        Dictionary<string, string>? deserialized = JsonSerializer.Deserialize<Dictionary<string, string>>(propsJson);
                        if (deserialized != null)
                        {
                            properties = new Dictionary<string, string>(deserialized, StringComparer.OrdinalIgnoreCase);
                        }
                    }
                }
            }
            catch
            {
                // Fallback to defaults
            }

            return (confidence, properties);
        }
    }
}
