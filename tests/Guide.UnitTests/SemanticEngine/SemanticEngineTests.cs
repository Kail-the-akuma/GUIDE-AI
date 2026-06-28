using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Guide.Core.Models;
using Guide.Semantic;
using Guide.Semantic.Classifiers;
using Xunit;

namespace Guide.UnitTests.SemanticEngine
{
    public class SemanticEngineTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _dbPath;
        private readonly string _connectionString;
        private readonly SqliteKnowledgeStore _store;

        public SemanticEngineTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "Guide_Test_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);

            _dbPath = Path.Combine(_tempDir, "knowledge.db");
            _connectionString = $"Data Source={_dbPath};Cache=Shared;Mode=ReadWriteCreate;";
            _store = new SqliteKnowledgeStore(_connectionString);
        }

        public void Dispose()
        {
            // Close connection pool and clean up files
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
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
        public async Task TestFullWorkflow_ParseStoreBfsAndDelta()
        {
            // --- 1. SET UP MOCK C# FILES ---
            var fileRepo = Path.Combine(_tempDir, "IUserRepository.cs");
            var fileService = Path.Combine(_tempDir, "UserService.cs");
            var fileIService = Path.Combine(_tempDir, "IUserService.cs");

            await File.WriteAllTextAsync(fileRepo, @"
namespace MyApp.Core
{
    public interface IUserRepository
    {
        public void Save();
    }
}");

            await File.WriteAllTextAsync(fileService, @"
using System.Threading.Tasks;

namespace MyApp.Core
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _repo;

        public UserService(IUserRepository repo)
        {
            _repo = repo;
        }

        public public void SaveUser()
        {
            _repo.Save();
        }
    }
}"); // Note: syntax error (public public void SaveUser) to test resilience

            await File.WriteAllTextAsync(fileIService, @"
namespace MyApp.Core
{
    public interface IUserService
    {
        public void SaveUser();
    }
}");

            // --- 2. AST PARSING (FallbackAstParser) ---
            var parser = new FallbackAstParser(_store);
            var parsedTrees = (await parser.ParseDirectoryAsync(_tempDir, CancellationToken.None)).ToList();

            // We expect 3 parsed trees because they are new
            Assert.Equal(3, parsedTrees.Count);

            // --- 3. KNOWLEDGE EXTRACTION ---
            var extractor = new KnowledgeExtractor();
            var knowledge = extractor.Extract(parsedTrees);

            Assert.Equal(3, knowledge.Nodes.Count());

            var userServiceNode = knowledge.Nodes.FirstOrDefault(n => n.Name == "UserService");
            Assert.NotNull(userServiceNode);
            Assert.Equal("Class", userServiceNode.NodeType);
            Assert.Equal("MyApp.Core", userServiceNode.Namespace);

            // Check public signatures of UserService (the constructor and public method should be parsed despite syntax errors)
            Assert.Contains("public UserService(IUserRepository repo)", userServiceNode.PublicSignatures);

            // Check relations
            var implementsEdge = knowledge.Edges.FirstOrDefault(e => e.RelationType == "Implements");
            Assert.NotNull(implementsEdge);
            Assert.Equal(userServiceNode.Id, implementsEdge.FromNodeId);

            var injectsEdge = knowledge.Edges.FirstOrDefault(e => e.RelationType == "Injects");
            Assert.NotNull(injectsEdge);
            Assert.Equal(userServiceNode.Id, injectsEdge.FromNodeId);

            // --- 4. PERSIST TO SQLITE STORE ---
            await _store.SaveGraphSnapshotAsync(1, knowledge);

            var latestVersion = await _store.GetLatestGraphVersionAsync();
            Assert.Equal(1, latestVersion);

            var retrievedSnapshot = await _store.GetSnapshotAsync(1);
            Assert.Equal(3, retrievedSnapshot.Nodes.Count());
            Assert.Equal(knowledge.Edges.Count(), retrievedSnapshot.Edges.Count());

            // --- 5. INCREMENTAL HASHING & SKIPPING ---
            // Parsing again without changes should return 0 parsed trees
            var reParsedTrees = (await parser.ParseDirectoryAsync(_tempDir, CancellationToken.None)).ToList();
            Assert.Empty(reParsedTrees);

            // Modifying UserService.cs should trigger parsing for only UserService.cs
            // We sleep slightly to make sure the write time is distinct
            await Task.Delay(10);
            await File.WriteAllTextAsync(fileService, @"
namespace MyApp.Core
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _repo;

        public UserService(IUserRepository repo)
        {
            _repo = repo;
        }

        public void SaveUser()
        {
            _repo.Save();
        }

        public void AnotherMethod() {}
    }
}");
            var parsedAfterModify = (await parser.ParseDirectoryAsync(_tempDir, CancellationToken.None)).ToList();
            Assert.Single(parsedAfterModify);
            Assert.Equal(fileService, parsedAfterModify[0].FilePath);

            // --- 6. MERGE SNAPSHOT INCREMENTALLY ---
            var oldSnapshot = await _store.GetSnapshotAsync(1);
            var modifiedFiles = parsedAfterModify.Select(t => t.FilePath).ToHashSet();

            // Seed extractor with old snapshot nodes to resolve references to unmodified files
            var incrementalExtractor = new KnowledgeExtractor(oldSnapshot.Nodes);
            var newKnowledge = incrementalExtractor.Extract(parsedAfterModify);

            var mergedKnowledge = MergeKnowledge(oldSnapshot, newKnowledge, modifiedFiles);
            await _store.SaveGraphSnapshotAsync(2, mergedKnowledge);

            var snapshot2 = await _store.GetSnapshotAsync(2);
            Assert.Equal(3, snapshot2.Nodes.Count());

            var updatedUserNode = snapshot2.Nodes.FirstOrDefault(n => n.Name == "UserService");
            Assert.NotNull(updatedUserNode);
            Assert.Contains("public void AnotherMethod()", updatedUserNode.PublicSignatures);

            // --- 7. BFS TRAVERSAL (ContextEngine) ---
            var contextEngine = new ContextEngine(_store);
            var contextResult = await contextEngine.BuildContextAsync("UserService", 1);

            // Anchored on UserService, at depth 1 it should reach IUserService and IUserRepository
            Assert.Contains(fileIService, contextResult.TargetFiles);
            Assert.Contains(fileRepo, contextResult.TargetFiles);
            Assert.Equal(2, contextResult.Explanations.Count());

            // --- 8. BFS CYCLE DETECTION ---
            // Setup a cyclical graph manually
            var cyclNodes = new List<RichNode>
            {
                new RichNode(1, "A.cs", "Cycle", "NodeA", "Class", ""),
                new RichNode(2, "B.cs", "Cycle", "NodeB", "Class", ""),
                new RichNode(3, "C.cs", "Cycle", "NodeC", "Class", "")
            };
            var cyclEdges = new List<RichEdge>
            {
                new RichEdge(1, 2, "DependsOn"),
                new RichEdge(2, 3, "DependsOn"),
                new RichEdge(3, 2, "DependsOn") // cycle B <-> C!
            };

            await _store.SaveGraphSnapshotAsync(3, new ExtractedKnowledge(cyclNodes, cyclEdges));

            // Run BFS starting from NodeA up to depth 3
            var cycleContext = await contextEngine.BuildContextAsync("NodeA", 3);

            // Should visit NodeB and NodeC without getting stuck in a loop
            Assert.Contains("B.cs", cycleContext.TargetFiles);
            Assert.Contains("C.cs", cycleContext.TargetFiles);
            Assert.Equal(2, cycleContext.Explanations.Count()); // NodeB (depth 1) and NodeC (depth 2), NodeA at depth 3 is visited but already in visited set

            // --- 9. DELTA-BASED INCREMENTAL CONTEXTS ---
            var contextDepth1 = await contextEngine.BuildContextAsync("NodeA", 1);
            var contextDepth2 = await contextEngine.BuildContextAsync("NodeA", 2);

            // Delta between depth 2 and depth 1 context
            var deltaContext = await contextEngine.BuildContextAsync("NodeA", 2, contextDepth1);

            // TargetFiles in delta should only have the new files discovered at depth 2 (NodeC)
            Assert.Contains("C.cs", deltaContext.TargetFiles);
            Assert.DoesNotContain("B.cs", deltaContext.TargetFiles);
            Assert.DoesNotContain("A.cs", deltaContext.TargetFiles);
        }

        private static ExtractedKnowledge MergeKnowledge(ExtractedKnowledge oldKnowledge, ExtractedKnowledge newKnowledge, HashSet<string> modifiedFiles)
        {
            var remainingOldNodes = oldKnowledge.Nodes
                .Where(n => n.Id.HasValue && !modifiedFiles.Contains(n.FilePath))
                .ToList();

            var remainingOldNodeIds = remainingOldNodes.Select(n => n.Id!.Value).ToHashSet();

            var remainingOldEdges = oldKnowledge.Edges
                .Where(e => e.FromNodeId.HasValue && e.ToNodeId.HasValue &&
                            remainingOldNodeIds.Contains(e.FromNodeId.Value) &&
                            remainingOldNodeIds.Contains(e.ToNodeId.Value))
                .ToList();

            var allNodes = new List<RichNode>();
            var idMap = new Dictionary<int, int>();
            int newIdCounter = 1;

            foreach (var node in remainingOldNodes)
            {
                int oldId = node.Id!.Value;
                var reindexedNode = node with { Id = newIdCounter };
                idMap[oldId] = newIdCounter;
                allNodes.Add(reindexedNode);
                newIdCounter++;
            }

            var newIdMap = new Dictionary<int, int>();
            foreach (var node in newKnowledge.Nodes)
            {
                if (node.Id.HasValue)
                {
                    int tempId = node.Id.Value;
                    var reindexedNode = node with { Id = newIdCounter };
                    newIdMap[tempId] = newIdCounter;
                    allNodes.Add(reindexedNode);
                    newIdCounter++;
                }
            }

            var allEdges = new List<RichEdge>();

            foreach (var edge in remainingOldEdges)
            {
                int newFrom = idMap[edge.FromNodeId!.Value];
                int newTo = idMap[edge.ToNodeId!.Value];
                allEdges.Add(edge with { FromNodeId = newFrom, ToNodeId = newTo });
            }

            foreach (var edge in newKnowledge.Edges)
            {
                int? newFrom = null;
                if (edge.FromNodeId.HasValue)
                {
                    if (newIdMap.TryGetValue(edge.FromNodeId.Value, out var valFrom))
                    {
                        newFrom = valFrom;
                    }
                    else if (idMap.TryGetValue(edge.FromNodeId.Value, out var oValFrom))
                    {
                        newFrom = oValFrom;
                    }
                }

                int? newTo = null;
                if (edge.ToNodeId.HasValue)
                {
                    if (newIdMap.TryGetValue(edge.ToNodeId.Value, out var valTo))
                    {
                        newTo = valTo;
                    }
                    else if (idMap.TryGetValue(edge.ToNodeId.Value, out var oValTo))
                    {
                        newTo = oValTo;
                    }
                }

                if (newFrom.HasValue && newTo.HasValue)
                {
                    allEdges.Add(edge with { FromNodeId = newFrom, ToNodeId = newTo });
                }
            }

            return new ExtractedKnowledge(allNodes, allEdges);
        }

        [Fact]
        public void ApiControllerClassifier_ShouldClassifyCorrectly()
        {
            var classifier = new ApiControllerClassifier();

            // Test suffix
            var tree1 = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("public class MyApiController {}");
            var classDecl1 = tree1.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>().First();
            Assert.True(classifier.Classify(classDecl1, out var conf1, out var det1, out var props1));
            Assert.Equal(1.0, conf1);
            Assert.Equal("ApiControllerClassifier", det1);
            Assert.Equal("SuffixController", props1["Rule"]);

            // Test inheritance
            var tree2 = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("public class CustomService : ControllerBase {}");
            var classDecl2 = tree2.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>().First();
            Assert.True(classifier.Classify(classDecl2, out var conf2, out var det2, out var props2));
            Assert.Equal("InheritsControllerBase", props2["Rule"]);

            // Test attributes
            var tree3 = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("[Route(\"api/[controller]\")] public class CustomApi {}");
            var classDecl3 = tree3.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>().First();
            Assert.True(classifier.Classify(classDecl3, out var conf3, out var det3, out var props3));
            Assert.Equal("RouteAttributes", props3["Rule"]);
        }

        [Fact]
        public void RepositoryClassifier_ShouldClassifyCorrectly()
        {
            var classifier = new RepositoryClassifier();

            var tree1 = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("public class UserRepository {}");
            var classDecl1 = tree1.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>().First();
            Assert.True(classifier.Classify(classDecl1, out var conf1, out var det1, out var props1));
            Assert.Equal("SuffixRepository", props1["Rule"]);

            var tree2 = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("public class CustomClass : IRepository<User> {}");
            var classDecl2 = tree2.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>().First();
            Assert.True(classifier.Classify(classDecl2, out var conf2, out var det2, out var props2));
            Assert.Equal("InheritsIRepository", props2["Rule"]);
        }

        [Fact]
        public void CqrsClassifier_ShouldClassifyCorrectly()
        {
            var classifier = new CqrsClassifier();

            var tree1 = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("public class CreateUserCommand {}");
            var classDecl1 = tree1.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>().First();
            Assert.True(classifier.Classify(classDecl1, out var conf1, out var det1, out var props1));
            Assert.Equal(NodeType.Feature, classifier.TargetType);
            Assert.Equal("SuffixCqrs", props1["Rule"]);

            var tree2 = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("public class CustomClass : IRequest<Unit> {}");
            var classDecl2 = tree2.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>().First();
            Assert.True(classifier.Classify(classDecl2, out var conf2, out var det2, out var props2));
            Assert.Equal("InheritsCqrsInterface", props2["Rule"]);
        }

        [Fact]
        public void EntityClassifier_ShouldClassifyCorrectly()
        {
            var classifier = new EntityClassifier();

            var tree1 = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("public class UserEntity {}");
            var classDecl1 = tree1.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>().First();
            Assert.True(classifier.Classify(classDecl1, out var conf1, out var det1, out var props1));
            Assert.Equal("SuffixEntity", props1["Rule"]);

            var tree2 = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("public class Order : AggregateRoot {}");
            var classDecl2 = tree2.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>().First();
            Assert.True(classifier.Classify(classDecl2, out var conf2, out var det2, out var props2));
            Assert.Equal("InheritsEntityOrAggregateRoot", props2["Rule"]);
        }

        [Fact]
        public void TestClassifier_ShouldClassifyCorrectly()
        {
            var classifier = new TestClassifier();

            var tree1 = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("public class UserServiceTests {}");
            var classDecl1 = tree1.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>().First();
            Assert.True(classifier.Classify(classDecl1, out var conf1, out var det1, out var props1));
            Assert.Equal("SuffixTests", props1["Rule"]);

            var tree2 = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("public class UserServiceSpec { [Fact] public void Test1() {} }");
            var classDecl2 = tree2.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>().First();
            Assert.True(classifier.Classify(classDecl2, out var conf2, out var det2, out var props2));
            Assert.Equal("TestAttributes", props2["Rule"]);
        }

        [Fact]
        public void PlaywrightTestClassifier_ShouldClassifyCorrectly()
        {
            var classifier = new PlaywrightTestClassifier();

            // Rule 1: Suffix "UiTests" (case-insensitive)
            var tree1 = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("public class LoginUiTests {}");
            var classDecl1 = tree1.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>().First();
            Assert.True(classifier.Classify(classDecl1, out var conf1, out var det1, out var props1));
            Assert.Equal(NodeType.PlaywrightTest, classifier.TargetType);
            Assert.Equal(1.0, conf1);
            Assert.Equal("PlaywrightTestClassifier", det1);
            Assert.Equal("SuffixUiOrE2eTests", props1["Rule"]);

            // Rule 1: Suffix "E2eTests" (case-insensitive)
            var tree2 = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("public class CheckoutE2ETests {}");
            var classDecl2 = tree2.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>().First();
            Assert.True(classifier.Classify(classDecl2, out var conf2, out var det2, out var props2));
            Assert.Equal("SuffixUiOrE2eTests", props2["Rule"]);

            // Rule 2: Inheritance "PageTest" (case-insensitive)
            var tree3 = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("public class CustomSpec : pagetest {}");
            var classDecl3 = tree3.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>().First();
            Assert.True(classifier.Classify(classDecl3, out var conf3, out var det3, out var props3));
            Assert.Equal("InheritsPageTest", props3["Rule"]);

            // Rule 2: Inheritance "PlaywrightTest" (case-insensitive)
            var tree4 = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("public class OtherSpec : PlaywrightTest {}");
            var classDecl4 = tree4.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>().First();
            Assert.True(classifier.Classify(classDecl4, out var conf4, out var det4, out var props4));
            Assert.Equal("InheritsPageTest", props4["Rule"]);

            // Negative case
            var tree5 = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("public class SomeRegularTests {}");
            var classDecl5 = tree5.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>().First();
            Assert.False(classifier.Classify(classDecl5, out _, out _, out _));
        }

        [Fact]
        public async Task SqliteKnowledgeStore_ShouldSaveAndRetrieveConfidenceAndProperties()
        {
            var nodeProps = new Dictionary<string, string> { { "Key1", "Value1" } };
            var edgeProps = new Dictionary<string, string> { { "Key2", "Value2" } };

            var node = new RichNode(
                Id: 10,
                FilePath: "test.cs",
                Namespace: "MyNamespace",
                Name: "MyClass",
                NodeType: "API",
                PublicSignatures: "public void Get()",
                Confidence: 0.85,
                DetectionMethod: "TestClassifier",
                Properties: nodeProps
            );

            var edge = new RichEdge(
                FromNodeId: 10,
                ToNodeId: 20,
                RelationType: "DependsOn",
                Confidence: 0.95,
                DetectionMethod: "Manual",
                Properties: edgeProps
            );

            var knowledge = new ExtractedKnowledge(
                new[] { node },
                new[] { edge }
            );

            await _store.SaveGraphSnapshotAsync(5, knowledge);

            var snapshot = await _store.GetSnapshotAsync(5);

            var retrievedNode = Assert.Single(snapshot.Nodes);
            Assert.Equal(10, retrievedNode.Id);
            Assert.Equal(0.85, retrievedNode.Confidence);
            Assert.Equal("TestClassifier", retrievedNode.DetectionMethod);
            Assert.Equal("Value1", retrievedNode.Properties["Key1"]);

            var retrievedEdge = Assert.Single(snapshot.Edges);
            Assert.Equal(10, retrievedEdge.FromNodeId);
            Assert.Equal(20, retrievedEdge.ToNodeId);
            Assert.Equal(0.95, retrievedEdge.Confidence);
            Assert.Equal("Value2", retrievedEdge.Properties["Key2"]);
        }

        [Fact]
        public async Task ProjectParser_ShouldFallbackToAstParserOnFailure()
        {
            var parser = new ProjectParser(_store);
            var result = await parser.ParseProjectSourcesAsync("non_existent_solution.sln", CancellationToken.None);

            // Should fall back to crawling current path or returning empty instead of throwing exceptions
            Assert.NotNull(result);
        }

        [Fact]
        public async Task TypeScriptParser_ShouldParseMultiLineImports()
        {
            var parser = new TypeScriptParser();
            var tempFile = Path.Combine(_tempDir, "test.ts");
            await File.WriteAllTextAsync(tempFile, @"
import {
    Foo,
    Bar
} from './foo-bar';

import
{
    Baz
}
from './baz';

import('./dynamic-multi'
);

import(
    './dynamic-with-options',
    { assert: { type: 'json' } }
);

import './static-simple';
");

            var context = await parser.GetContextAsync(tempFile);
            Assert.Contains("./foo-bar", context.Imports);
            Assert.Contains("./baz", context.Imports);
            Assert.Contains("./dynamic-multi", context.Imports);
            Assert.Contains("./dynamic-with-options", context.Imports);
            Assert.Contains("./static-simple", context.Imports);
            Assert.Equal(5, context.Imports.Count);
        }
    }
}
