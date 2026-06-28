using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Guide.Core.Interfaces;
using Guide.Core.Models;

namespace Guide.Semantic
{
    public class SqliteKnowledgeStore : IKnowledgeStore
    {
        private readonly string _connectionString;
        private readonly string _sqliteConnectionString;

        public SqliteKnowledgeStore(string connectionString)
        {
            _connectionString = connectionString;
            _sqliteConnectionString = connectionString
                .Replace("Busy Timeout=5000;", "", StringComparison.OrdinalIgnoreCase)
                .Replace("Busy Timeout=5000", "", StringComparison.OrdinalIgnoreCase);
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using var connection = new SqliteConnection(_sqliteConnectionString);
            connection.Open();

            // Execute WAL and busy_timeout immediately on the connection
            using (var pragmaCmd = connection.CreateCommand())
            {
                pragmaCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
                pragmaCmd.ExecuteNonQuery();
            }

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Files (
                    Path TEXT PRIMARY KEY,
                    LastWriteTime INTEGER NOT NULL,
                    Hash TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS Nodes (
                    Id INTEGER NOT NULL,
                    FilePath TEXT NOT NULL,
                    Namespace TEXT,
                    Name TEXT NOT NULL,
                    NodeType TEXT NOT NULL,
                    PublicSignatures TEXT,
                    Confidence REAL,
                    DetectionMethod TEXT,
                    Properties TEXT,
                    Version INTEGER NOT NULL,
                    PRIMARY KEY (Id, Version)
                );
                CREATE TABLE IF NOT EXISTS Edges (
                    FromNodeId INTEGER NOT NULL,
                    ToNodeId INTEGER NOT NULL,
                    RelationType TEXT NOT NULL,
                    Confidence REAL,
                    Properties TEXT,
                    Version INTEGER NOT NULL,
                    PRIMARY KEY (FromNodeId, ToNodeId, RelationType, Version)
                );
                CREATE TABLE IF NOT EXISTS FeatureFlows (
                    FeatureName TEXT NOT NULL,
                    NodeName TEXT NOT NULL,
                    PRIMARY KEY (FeatureName, NodeName)
                );
            ";
            cmd.ExecuteNonQuery();

            // Run migration ALTER queries if the database was created with the old schema.
            try
            {
                using var alterCmd = connection.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE Nodes ADD COLUMN Confidence REAL;";
                alterCmd.ExecuteNonQuery();
            }
            catch (SqliteException) { /* Column might already exist */ }

            try
            {
                using var alterCmd = connection.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE Nodes ADD COLUMN DetectionMethod TEXT;";
                alterCmd.ExecuteNonQuery();
            }
            catch (SqliteException) { /* Column might already exist */ }

            try
            {
                using var alterCmd = connection.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE Nodes ADD COLUMN Properties TEXT;";
                alterCmd.ExecuteNonQuery();
            }
            catch (SqliteException) { /* Column might already exist */ }

            try
            {
                using var alterCmd = connection.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE Edges ADD COLUMN Confidence REAL;";
                alterCmd.ExecuteNonQuery();
            }
            catch (SqliteException) { /* Column might already exist */ }

            try
            {
                using var alterCmd = connection.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE Edges ADD COLUMN Properties TEXT;";
                alterCmd.ExecuteNonQuery();
            }
            catch (SqliteException) { /* Column might already exist */ }
        }

        private async Task<SqliteConnection> GetOpenConnectionAsync()
        {
            var connection = new SqliteConnection(_sqliteConnectionString);
            await connection.OpenAsync();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
                await cmd.ExecuteNonQueryAsync();
            }
            return connection;
        }

        public async Task SaveGraphSnapshotAsync(int version, ExtractedKnowledge knowledge)
        {
            using var connection = await GetOpenConnectionAsync();
            using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
            try
            {
                // Delete existing data for this version to ensure idempotency
                using (var deleteNodesCmd = connection.CreateCommand())
                {
                    deleteNodesCmd.Transaction = transaction;
                    deleteNodesCmd.CommandText = "DELETE FROM Nodes WHERE Version = @Version";
                    deleteNodesCmd.Parameters.AddWithValue("@Version", version);
                    await deleteNodesCmd.ExecuteNonQueryAsync();
                }
                using (var deleteEdgesCmd = connection.CreateCommand())
                {
                    deleteEdgesCmd.Transaction = transaction;
                    deleteEdgesCmd.CommandText = "DELETE FROM Edges WHERE Version = @Version";
                    deleteEdgesCmd.Parameters.AddWithValue("@Version", version);
                    await deleteEdgesCmd.ExecuteNonQueryAsync();
                }

                // Insert nodes
                foreach (var node in knowledge.Nodes)
                {
                    using var insertNodeCmd = connection.CreateCommand();
                    insertNodeCmd.Transaction = transaction;
                    insertNodeCmd.CommandText = @"
                        INSERT INTO Nodes (Id, FilePath, Namespace, Name, NodeType, PublicSignatures, Confidence, DetectionMethod, Properties, Version)
                        VALUES (@Id, @FilePath, @Namespace, @Name, @NodeType, @PublicSignatures, @Confidence, @DetectionMethod, @Properties, @Version)";
                    insertNodeCmd.Parameters.AddWithValue("@Id", node.Id ?? 0);
                    insertNodeCmd.Parameters.AddWithValue("@FilePath", node.FilePath);
                    insertNodeCmd.Parameters.AddWithValue("@Namespace", node.Namespace ?? (object)DBNull.Value);
                    insertNodeCmd.Parameters.AddWithValue("@Name", node.Name);
                    insertNodeCmd.Parameters.AddWithValue("@NodeType", node.NodeType);
                    insertNodeCmd.Parameters.AddWithValue("@PublicSignatures", node.PublicSignatures ?? (object)DBNull.Value);
                    insertNodeCmd.Parameters.AddWithValue("@Confidence", node.Confidence);
                    insertNodeCmd.Parameters.AddWithValue("@DetectionMethod", node.DetectionMethod);
                    var nodePropsJson = node.Properties != null ? JsonSerializer.Serialize(node.Properties) : "{}";
                    insertNodeCmd.Parameters.AddWithValue("@Properties", nodePropsJson);
                    insertNodeCmd.Parameters.AddWithValue("@Version", version);
                    await insertNodeCmd.ExecuteNonQueryAsync();
                }

                // Insert edges
                foreach (var edge in knowledge.Edges)
                {
                    using var insertEdgeCmd = connection.CreateCommand();
                    insertEdgeCmd.Transaction = transaction;
                    insertEdgeCmd.CommandText = @"
                        INSERT INTO Edges (FromNodeId, ToNodeId, RelationType, Confidence, Properties, Version)
                        VALUES (@FromNodeId, @ToNodeId, @RelationType, @Confidence, @Properties, @Version)";
                    insertEdgeCmd.Parameters.AddWithValue("@FromNodeId", edge.FromNodeId ?? 0);
                    insertEdgeCmd.Parameters.AddWithValue("@ToNodeId", edge.ToNodeId ?? 0);
                    insertEdgeCmd.Parameters.AddWithValue("@RelationType", edge.RelationType);
                    insertEdgeCmd.Parameters.AddWithValue("@Confidence", edge.Confidence);
                    var edgePropsJson = edge.Properties != null ? JsonSerializer.Serialize(edge.Properties) : "{}";
                    insertEdgeCmd.Parameters.AddWithValue("@Properties", edgePropsJson);
                    insertEdgeCmd.Parameters.AddWithValue("@Version", version);
                    await insertEdgeCmd.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<int> GetLatestGraphVersionAsync()
        {
            using var connection = await GetOpenConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT IFNULL(MAX(Version), 0) FROM Nodes";
            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

        public async Task<ExtractedKnowledge> GetSnapshotAsync(int version)
        {
            using var connection = await GetOpenConnectionAsync();

            var nodes = new List<RichNode>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT Id, FilePath, Namespace, Name, NodeType, PublicSignatures, Confidence, DetectionMethod, Properties FROM Nodes WHERE Version = @Version";
                cmd.Parameters.AddWithValue("@Version", version);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    double confidence = reader.IsDBNull(6) ? 1.0 : reader.GetDouble(6);
                    string detectionMethod = reader.IsDBNull(7) ? "Manual" : reader.GetString(7);
                    string propertiesJson = reader.IsDBNull(8) ? "{}" : reader.GetString(8);
                    Dictionary<string, string>? properties = null;
                    try
                    {
                        properties = JsonSerializer.Deserialize<Dictionary<string, string>>(propertiesJson);
                    }
                    catch { /* Fallback */ }

                    nodes.Add(new RichNode(
                        reader.GetInt32(0),
                        reader.GetString(1),
                        reader.IsDBNull(2) ? "" : reader.GetString(2),
                        reader.GetString(3),
                        reader.GetString(4),
                        reader.IsDBNull(5) ? "" : reader.GetString(5),
                        confidence,
                        detectionMethod,
                        properties
                    ));
                }
            }

            var edges = new List<RichEdge>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT FromNodeId, ToNodeId, RelationType, Confidence, Properties FROM Edges WHERE Version = @Version";
                cmd.Parameters.AddWithValue("@Version", version);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    double confidence = reader.IsDBNull(3) ? 1.0 : reader.GetDouble(3);
                    string propertiesJson = reader.IsDBNull(4) ? "{}" : reader.GetString(4);
                    Dictionary<string, string>? properties = null;
                    try
                    {
                        properties = JsonSerializer.Deserialize<Dictionary<string, string>>(propertiesJson);
                    }
                    catch { /* Fallback */ }

                    edges.Add(new RichEdge(
                        reader.GetInt32(0),
                        reader.GetInt32(1),
                        reader.GetString(2),
                        confidence,
                        "Manual",
                        properties
                    ));
                }
            }

            return new ExtractedKnowledge(nodes, edges);
        }

        public async Task MapFeatureFlowAsync(string featureName, IEnumerable<string> relatedNodes)
        {
            using var connection = await GetOpenConnectionAsync();
            using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
            try
            {
                using (var deleteCmd = connection.CreateCommand())
                {
                    deleteCmd.Transaction = transaction;
                    deleteCmd.CommandText = "DELETE FROM FeatureFlows WHERE FeatureName = @FeatureName";
                    deleteCmd.Parameters.AddWithValue("@FeatureName", featureName);
                    await deleteCmd.ExecuteNonQueryAsync();
                }

                foreach (var node in relatedNodes)
                {
                    using var insertCmd = connection.CreateCommand();
                    insertCmd.Transaction = transaction;
                    insertCmd.CommandText = "INSERT INTO FeatureFlows (FeatureName, NodeName) VALUES (@FeatureName, @NodeName)";
                    insertCmd.Parameters.AddWithValue("@FeatureName", featureName);
                    insertCmd.Parameters.AddWithValue("@NodeName", node);
                    await insertCmd.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<IEnumerable<string>> GetFeatureFlowAsync(string featureName)
        {
            using var connection = await GetOpenConnectionAsync();
            var result = new List<string>();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT NodeName FROM FeatureFlows WHERE FeatureName = @FeatureName";
            cmd.Parameters.AddWithValue("@FeatureName", featureName);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(reader.GetString(0));
            }
            return result;
        }

        // Helper methods for file indexer hashes (used by FallbackAstParser)
        public async Task<string?> GetFileHashAsync(string path, long lastWriteTime)
        {
            using var connection = await GetOpenConnectionAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Hash FROM Files WHERE Path = @Path AND LastWriteTime = @LastWriteTime";
            cmd.Parameters.AddWithValue("@Path", path);
            cmd.Parameters.AddWithValue("@LastWriteTime", lastWriteTime);
            var result = await cmd.ExecuteScalarAsync();
            return result?.ToString();
        }

        public async Task SaveFileHashAsync(string path, long lastWriteTime, string hash)
        {
            using var connection = await GetOpenConnectionAsync();
            using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = @"
                    INSERT INTO Files (Path, LastWriteTime, Hash)
                    VALUES (@Path, @LastWriteTime, @Hash)
                    ON CONFLICT(Path) DO UPDATE SET LastWriteTime = excluded.LastWriteTime, Hash = excluded.Hash";
                cmd.Parameters.AddWithValue("@Path", path);
                cmd.Parameters.AddWithValue("@LastWriteTime", lastWriteTime);
                cmd.Parameters.AddWithValue("@Hash", hash);
                await cmd.ExecuteNonQueryAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }
}
