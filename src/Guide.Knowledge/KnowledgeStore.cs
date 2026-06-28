using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Guide.Core.Interfaces;
using Guide.Core.Models;

namespace Guide.Knowledge
{
    public class KnowledgeStore : IEngineeringKnowledge
    {
        private readonly string _sqliteConnectionString;

        public KnowledgeStore(string connectionString)
        {
            _sqliteConnectionString = connectionString
                .Replace("Busy Timeout=5000;", "", StringComparison.OrdinalIgnoreCase)
                .Replace("Busy Timeout=5000", "", StringComparison.OrdinalIgnoreCase);
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using var connection = new SqliteConnection(_sqliteConnectionString);
            connection.Open();

            // Enforce WAL mode and busy_timeout immediately
            using (var pragmaCmd = connection.CreateCommand())
            {
                pragmaCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
                pragmaCmd.ExecuteNonQuery();
            }

            // 1. Ensure the Nodes table exists (in case SqliteKnowledgeStore hasn't run yet)
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Nodes (
                        Id INTEGER NOT NULL,
                        FilePath TEXT NOT NULL,
                        Namespace TEXT,
                        Name TEXT NOT NULL,
                        NodeType TEXT NOT NULL,
                        PublicSignatures TEXT,
                        Version INTEGER NOT NULL,
                        PRIMARY KEY (Id, Version)
                    );
                ";
                cmd.ExecuteNonQuery();
            }

            // 2. Ensure schema contains the Confidence, DetectionMethod and Properties columns
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(Nodes);";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    columns.Add(reader.GetString(1));
                }
            }

            if (!columns.Contains("Confidence"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Nodes ADD COLUMN Confidence REAL DEFAULT 1.0;";
                cmd.ExecuteNonQuery();
            }
            if (!columns.Contains("DetectionMethod"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Nodes ADD COLUMN DetectionMethod TEXT DEFAULT 'Manual';";
                cmd.ExecuteNonQuery();
            }
            if (!columns.Contains("Properties"))
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE Nodes ADD COLUMN Properties TEXT;";
                cmd.ExecuteNonQuery();
            }

            // 3. Create or maintain FTS5 virtual table "KnowledgeSearch"
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE VIRTUAL TABLE IF NOT EXISTS KnowledgeSearch USING fts5(
                        Id,
                        Title,
                        Content,
                        Category
                    );
                ";
                cmd.ExecuteNonQuery();
            }
        }

        private async Task<int> GetStartIdAsync(SqliteConnection connection, SqliteTransaction transaction)
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "SELECT IFNULL(MAX(Id), 0) FROM Nodes WHERE NodeType != 'ADR'";
            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result) + 1;
        }

        private static string GetMetadataValue(Dictionary<string, string> dict, string key)
        {
            return dict.TryGetValue(key, out var val) ? val : "";
        }

        private static string GetPlainText(ContainerInline container)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var inline in container)
            {
                if (inline is LiteralInline literal)
                {
                    sb.Append(literal.Content.ToString());
                }
                else if (inline is CodeInline code)
                {
                    sb.Append(code.Content);
                }
                else if (inline is ContainerInline childContainer)
                {
                    sb.Append(GetPlainText(childContainer));
                }
                else
                {
                    sb.Append(inline.ToString());
                }
            }
            return sb.ToString().Trim();
        }

        private string SanitizeFtsQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return "";
            var cleanWords = query.Split(new[] { ' ', '\t', '\r', '\n', '"', '\'', '-', '*', ':', '(', ')', '[', ']', '{', '}' }, StringSplitOptions.RemoveEmptyEntries)
                                  .Select(w => w.Trim())
                                  .Where(w => w.Length > 0);
            return string.Join(" ", cleanWords);
        }

        public async Task SyncLocalDocsAsync(string knowledgeDirectoryPath)
        {
            // Determine the base agents directory path
            string agentsDir = knowledgeDirectoryPath;
            if (!agentsDir.EndsWith(".agents", StringComparison.OrdinalIgnoreCase) && Directory.Exists(Path.Combine(agentsDir, ".agents")))
            {
                agentsDir = Path.Combine(agentsDir, ".agents");
            }

            var files = new List<string>();

            // 1. Scan .agents/knowledge/ recursively
            string knowledgeDir = Path.Combine(agentsDir, "knowledge");
            if (Directory.Exists(knowledgeDir))
            {
                var mdFiles = Directory.GetFiles(knowledgeDir, "*.md", SearchOption.AllDirectories);
                files.AddRange(mdFiles);
            }

            // 2. Scan .agents/AGENTS.md
            string agentsMdFile = Path.Combine(agentsDir, "AGENTS.md");
            if (File.Exists(agentsMdFile))
            {
                files.Add(agentsMdFile);
            }

            var rules = new List<ParsedRule>();
            var pipeline = new MarkdownPipelineBuilder().UseYamlFrontMatter().Build();

            foreach (var filePath in files)
            {
                string content = await File.ReadAllTextAsync(filePath);
                var document = Markdown.Parse(content, pipeline);
                var yamlBlock = document.Descendants<YamlFrontMatterBlock>().FirstOrDefault();

                var frontmatter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (yamlBlock != null)
                {
                    var yamlText = content.Substring(yamlBlock.Span.Start, yamlBlock.Span.Length);
                    var yamlLines = yamlText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in yamlLines)
                    {
                        var trimmed = line.Trim();
                        if (trimmed == "---") continue;

                        var colonIdx = trimmed.IndexOf(':');
                        if (colonIdx > 0)
                        {
                            var key = trimmed.Substring(0, colonIdx).Trim();
                            var val = trimmed.Substring(colonIdx + 1).Trim();
                            if ((val.StartsWith("\"") && val.EndsWith("\"")) || (val.StartsWith("'") && val.EndsWith("'")))
                            {
                                val = val.Substring(1, val.Length - 2);
                            }
                            frontmatter[key] = val;
                        }
                    }
                }

                // Get Title from the first header block
                var headingBlock = document.Descendants<HeadingBlock>().FirstOrDefault();
                string title = "";
                if (headingBlock != null && headingBlock.Inline != null)
                {
                    title = GetPlainText(headingBlock.Inline);
                }
                if (string.IsNullOrWhiteSpace(title))
                {
                    title = Path.GetFileNameWithoutExtension(filePath);
                }

                // Get Body content (without frontmatter)
                string bodyContent = content;
                if (yamlBlock != null)
                {
                    int bodyStart = yamlBlock.Span.End + 1;
                    if (bodyStart < content.Length)
                    {
                        bodyContent = content.Substring(bodyStart).TrimStart();
                    }
                    else
                    {
                        bodyContent = "";
                    }
                }

                // Determine Category
                string category = "General";
                if (filePath.Contains("knowledge"))
                {
                    var relative = Path.GetRelativePath(knowledgeDir, filePath);
                    var dirName = Path.GetDirectoryName(relative);
                    if (!string.IsNullOrEmpty(dirName) && dirName != ".")
                    {
                        category = dirName;
                    }
                    else
                    {
                        category = "Knowledge";
                    }
                }

                rules.Add(new ParsedRule
                {
                    FilePath = filePath,
                    Title = title,
                    Content = bodyContent,
                    Category = category,
                    Metadata = frontmatter
                });
            }

            // Save to the database
            using var connection = new SqliteConnection(_sqliteConnectionString);
            await connection.OpenAsync();

            using (var pragmaCmd = connection.CreateCommand())
            {
                pragmaCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
                await pragmaCmd.ExecuteNonQueryAsync();
            }

            using var transaction = connection.BeginTransaction();
            try
            {
                // Get latest graph snapshot version (defaults to 1 if no version exists)
                int latestVersion = 1;
                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = "SELECT IFNULL(MAX(Version), 0) FROM Nodes";
                    var res = await cmd.ExecuteScalarAsync();
                    var val = Convert.ToInt32(res);
                    if (val > 0)
                    {
                        latestVersion = val;
                    }
                }

                // Delete existing ADR nodes for this version (idempotency)
                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = "DELETE FROM Nodes WHERE Version = @Version AND NodeType = 'ADR'";
                    cmd.Parameters.AddWithValue("@Version", latestVersion);
                    await cmd.ExecuteNonQueryAsync();
                }

                // Clear FTS5 table
                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = "DELETE FROM KnowledgeSearch";
                    await cmd.ExecuteNonQueryAsync();
                }

                int currentId = await GetStartIdAsync(connection, transaction);

                foreach (var rule in rules)
                {
                    var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Tags"] = GetMetadataValue(rule.Metadata, "Tags"),
                        ["Priority"] = GetMetadataValue(rule.Metadata, "Priority"),
                        ["Status"] = GetMetadataValue(rule.Metadata, "Status"),
                        ["AppliesTo"] = GetMetadataValue(rule.Metadata, "AppliesTo"),
                        ["Deprecated"] = GetMetadataValue(rule.Metadata, "Deprecated"),
                        ["Version"] = GetMetadataValue(rule.Metadata, "Version"),
                        ["Owner"] = GetMetadataValue(rule.Metadata, "Owner")
                    };

                    string propertiesJson = JsonSerializer.Serialize(properties);
                    string relPath = Path.GetRelativePath(knowledgeDirectoryPath, rule.FilePath);

                    // Insert RichNode in the SQLite graph
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.Transaction = transaction;
                        cmd.CommandText = @"
                            INSERT INTO Nodes (Id, FilePath, Namespace, Name, NodeType, PublicSignatures, Version, Confidence, DetectionMethod, Properties)
                            VALUES (@Id, @FilePath, @Namespace, @Name, @NodeType, @PublicSignatures, @Version, @Confidence, @DetectionMethod, @Properties)
                        ";
                        cmd.Parameters.AddWithValue("@Id", currentId);
                        cmd.Parameters.AddWithValue("@FilePath", relPath);
                        cmd.Parameters.AddWithValue("@Namespace", DBNull.Value);
                        cmd.Parameters.AddWithValue("@Name", rule.Title);
                        cmd.Parameters.AddWithValue("@NodeType", "ADR");
                        cmd.Parameters.AddWithValue("@PublicSignatures", DBNull.Value);
                        cmd.Parameters.AddWithValue("@Version", latestVersion);
                        cmd.Parameters.AddWithValue("@Confidence", 1.0);
                        cmd.Parameters.AddWithValue("@DetectionMethod", "Manual");
                        cmd.Parameters.AddWithValue("@Properties", propertiesJson);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    // Insert into KnowledgeSearch
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.Transaction = transaction;
                        cmd.CommandText = @"
                            INSERT INTO KnowledgeSearch (Id, Title, Content, Category)
                            VALUES (@Id, @Title, @Content, @Category)
                        ";
                        cmd.Parameters.AddWithValue("@Id", currentId.ToString());
                        cmd.Parameters.AddWithValue("@Title", rule.Title);
                        cmd.Parameters.AddWithValue("@Content", rule.Content);
                        cmd.Parameters.AddWithValue("@Category", rule.Category);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    currentId++;
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task RecordEntryAsync(string context, string ruleOrPitfall)
        {
            using var connection = new SqliteConnection(_sqliteConnectionString);
            await connection.OpenAsync();

            using (var pragmaCmd = connection.CreateCommand())
            {
                pragmaCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
                await pragmaCmd.ExecuteNonQueryAsync();
            }

            using var transaction = connection.BeginTransaction();
            try
            {
                int latestVersion = 1;
                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = "SELECT IFNULL(MAX(Version), 0) FROM Nodes";
                    var res = await cmd.ExecuteScalarAsync();
                    var val = Convert.ToInt32(res);
                    if (val > 0)
                    {
                        latestVersion = val;
                    }
                }

                int currentId = await GetStartIdAsync(connection, transaction);

                var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Tags"] = "",
                    ["Priority"] = "Normal",
                    ["Status"] = "Active",
                    ["AppliesTo"] = context,
                    ["Deprecated"] = "false",
                    ["Version"] = "1.0",
                    ["Owner"] = "Manual"
                };
                string propertiesJson = JsonSerializer.Serialize(properties);

                // Insert RichNode in the SQLite graph
                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO Nodes (Id, FilePath, Namespace, Name, NodeType, PublicSignatures, Version, Confidence, DetectionMethod, Properties)
                        VALUES (@Id, @FilePath, @Namespace, @Name, @NodeType, @PublicSignatures, @Version, @Confidence, @DetectionMethod, @Properties)
                    ";
                    cmd.Parameters.AddWithValue("@Id", currentId);
                    cmd.Parameters.AddWithValue("@FilePath", "RecordedEntry");
                    cmd.Parameters.AddWithValue("@Namespace", DBNull.Value);
                    cmd.Parameters.AddWithValue("@Name", context);
                    cmd.Parameters.AddWithValue("@NodeType", "ADR");
                    cmd.Parameters.AddWithValue("@PublicSignatures", DBNull.Value);
                    cmd.Parameters.AddWithValue("@Version", latestVersion);
                    cmd.Parameters.AddWithValue("@Confidence", 1.0);
                    cmd.Parameters.AddWithValue("@DetectionMethod", "Manual");
                    cmd.Parameters.AddWithValue("@Properties", propertiesJson);
                    await cmd.ExecuteNonQueryAsync();
                }

                // Insert into KnowledgeSearch
                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO KnowledgeSearch (Id, Title, Content, Category)
                        VALUES (@Id, @Title, @Content, @Category)
                    ";
                    cmd.Parameters.AddWithValue("@Id", currentId.ToString());
                    cmd.Parameters.AddWithValue("@Title", context);
                    cmd.Parameters.AddWithValue("@Content", ruleOrPitfall);
                    cmd.Parameters.AddWithValue("@Category", "Recorded");
                    await cmd.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<IEnumerable<KnowledgeEntry>> QueryKnowledgeAsync(string userIntent)
        {
            if (string.IsNullOrWhiteSpace(userIntent))
            {
                return Enumerable.Empty<KnowledgeEntry>();
            }

            var results = new List<KnowledgeEntry>();
            using var connection = new SqliteConnection(_sqliteConnectionString);
            await connection.OpenAsync();

            using (var pragmaCmd = connection.CreateCommand())
            {
                pragmaCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
                await pragmaCmd.ExecuteNonQueryAsync();
            }

            bool success = false;
            try
            {
                var sanitizedQuery = SanitizeFtsQuery(userIntent);
                if (!string.IsNullOrWhiteSpace(sanitizedQuery))
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT Title, Content, Category FROM KnowledgeSearch WHERE KnowledgeSearch MATCH @Query";
                    cmd.Parameters.AddWithValue("@Query", sanitizedQuery);
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        results.Add(new KnowledgeEntry(
                            reader.GetString(0),
                            reader.GetString(1),
                            reader.GetString(2)
                        ));
                    }
                    success = true;
                }
            }
            catch
            {
                success = false;
            }

            if (!success)
            {
                results.Clear();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT Title, Content, Category FROM KnowledgeSearch 
                    WHERE Title LIKE @Query OR Content LIKE @Query OR Category LIKE @Query";
                cmd.Parameters.AddWithValue("@Query", "%" + userIntent + "%");
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    results.Add(new KnowledgeEntry(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetString(2)
                    ));
                }
            }

            return results;
        }

        private class ParsedRule
        {
            public required string FilePath { get; set; }
            public required string Title { get; set; }
            public required string Content { get; set; }
            public required string Category { get; set; }
            public required Dictionary<string, string> Metadata { get; set; }
        }
    }
}
