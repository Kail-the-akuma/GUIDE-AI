using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Guide.Core.Models;
using Guide.Knowledge;
using Xunit;

namespace Guide.UnitTests.Knowledge
{
    public class KnowledgeStoreTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _dbPath;
        private readonly string _connectionString;
        private readonly KnowledgeStore _store;

        public KnowledgeStoreTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "Guide_KnowledgeTest_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);

            _dbPath = Path.Combine(_tempDir, "project_graph.db");
            _connectionString = $"Data Source={_dbPath};Cache=Shared;Mode=ReadWriteCreate;";
            _store = new KnowledgeStore(_connectionString);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        [Fact]
        public async Task TestSyncLocalDocs_ParsesMarkdownAndSavesToGraphAndFTS()
        {
            // 1. Create a mock .agents structure
            var agentsDir = Path.Combine(_tempDir, ".agents");
            var knowledgeDir = Path.Combine(agentsDir, "knowledge");
            var subFolderDir = Path.Combine(knowledgeDir, "architecture");
            Directory.CreateDirectory(subFolderDir);

            // Create a rule under .agents/knowledge/architecture/rule1.md
            var rule1Path = Path.Combine(subFolderDir, "rule1.md");
            await File.WriteAllTextAsync(rule1Path, @"---
Tags: arch, boundary
Priority: Critical
Status: Active
AppliesTo: SemanticEngine
Deprecated: false
Version: 2.1
Owner: Architect
---
# Boundary Enforcement Rule

Do not reference validators from semantic engine.");

            // Create .agents/AGENTS.md (no frontmatter)
            var agentsMdPath = Path.Combine(agentsDir, "AGENTS.md");
            await File.WriteAllTextAsync(agentsMdPath, @"# Agent Guidelines

Follow boundaries strictly.");

            // 2. Call SyncLocalDocsAsync (pass the root temp directory)
            await _store.SyncLocalDocsAsync(_tempDir);

            // 3. Verify in SQLite Nodes table
            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();

                // Read nodes
                var nodes = new List<(int Id, string FilePath, string Name, string NodeType, string Properties)>();
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "SELECT Id, FilePath, Name, NodeType, Properties FROM Nodes WHERE NodeType = 'ADR'";
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        nodes.Add((
                            reader.GetInt32(0),
                            reader.GetString(1),
                            reader.GetString(2),
                            reader.GetString(3),
                            reader.IsDBNull(4) ? "" : reader.GetString(4)
                        ));
                    }
                }

                Assert.Equal(2, nodes.Count);

                var rule1Node = nodes.FirstOrDefault(n => n.Name == "Boundary Enforcement Rule");
                Assert.NotNull(rule1Node.Name);
                Assert.Equal("ADR", rule1Node.NodeType);
                // Verify properties
                var props = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(rule1Node.Properties);
                Assert.NotNull(props);
                Assert.Equal("arch, boundary", props["Tags"]);
                Assert.Equal("Critical", props["Priority"]);
                Assert.Equal("Active", props["Status"]);
                Assert.Equal("SemanticEngine", props["AppliesTo"]);
                Assert.Equal("false", props["Deprecated"]);
                Assert.Equal("2.1", props["Version"]);
                Assert.Equal("Architect", props["Owner"]);

                var agentsNode = nodes.FirstOrDefault(n => n.Name == "Agent Guidelines");
                Assert.NotNull(agentsNode.Name);
                var agentsProps = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(agentsNode.Properties);
                Assert.NotNull(agentsProps);
                Assert.Equal("", agentsProps["Tags"]);
            }

            // 4. Verify FTS5 query
            var queryResults = (await _store.QueryKnowledgeAsync("boundary")).ToList();
            Assert.Single(queryResults);
            Assert.Equal("Boundary Enforcement Rule", queryResults[0].Title);
            Assert.Contains("Do not reference validators", queryResults[0].Content);
            Assert.Equal("architecture", queryResults[0].Category);

            var queryResults2 = (await _store.QueryKnowledgeAsync("Guidelines")).ToList();
            Assert.Single(queryResults2);
            Assert.Equal("Agent Guidelines", queryResults2[0].Title);
            Assert.Equal("General", queryResults2[0].Category);
        }

        [Fact]
        public async Task TestRecordEntry_SavesCorrectly()
        {
            await _store.RecordEntryAsync("Database", "Always close connection to prevent leaks.");

            var results = (await _store.QueryKnowledgeAsync("connection")).ToList();
            Assert.Single(results);
            Assert.Equal("Database", results[0].Title);
            Assert.Equal("Always close connection to prevent leaks.", results[0].Content);
            Assert.Equal("Recorded", results[0].Category);
        }
    }
}
