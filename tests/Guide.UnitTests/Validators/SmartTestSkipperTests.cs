using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Guide.Core.Interfaces;
using Guide.Core.Models;
using Guide.Validation;
using Xunit;

namespace Guide.UnitTests.Validators;

public class SmartTestSkipperTests
{
    private class TestCommandLineRunner : ICommandLineRunner
    {
        public Func<string, string, string?, Task<(int ExitCode, string Output)>>? OnExecute { get; set; }

        public Task<(int ExitCode, string Output)> ExecuteAsync(string command, string arguments, string? workingDirectory = null, CancellationToken ct = default)
        {
            if (OnExecute != null)
            {
                return OnExecute(command, arguments, workingDirectory);
            }
            return Task.FromResult((0, ""));
        }
    }

    private class TestKnowledgeStore : IKnowledgeStore
    {
        public Func<Task<int>>? OnGetLatestGraphVersion { get; set; }
        public Func<int, Task<ExtractedKnowledge>>? OnGetSnapshot { get; set; }

        public Task SaveGraphSnapshotAsync(int version, ExtractedKnowledge knowledge) => Task.CompletedTask;
        public Task<int> GetLatestGraphVersionAsync() => OnGetLatestGraphVersion != null ? OnGetLatestGraphVersion() : Task.FromResult(0);
        public Task<ExtractedKnowledge> GetSnapshotAsync(int version) => OnGetSnapshot != null ? OnGetSnapshot(version) : Task.FromResult(new ExtractedKnowledge(Array.Empty<RichNode>(), Array.Empty<RichEdge>()));
        public Task MapFeatureFlowAsync(string featureName, IEnumerable<string> relatedNodes) => Task.CompletedTask;
        public Task<IEnumerable<string>> GetFeatureFlowAsync(string featureName) => Task.FromResult(Enumerable.Empty<string>());
    }

    [Fact]
    public async Task GetTestFilterAsync_ReturnsNull_WhenNoCsFilesChanged()
    {
        // Arrange
        var mockRunner = new TestCommandLineRunner
        {
            OnExecute = (cmd, args, wd) => Task.FromResult((0, " M README.md\n?? design.txt\n"))
        };
        var mockStore = new TestKnowledgeStore();
        var skipper = new SmartTestSkipper(mockRunner, mockStore);

        // Act
        var result = await skipper.GetTestFilterAsync("solution.sln", "C:/repo", CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetTestFilterAsync_ReturnsEmptyString_WhenGitCommandFails()
    {
        // Arrange
        var mockRunner = new TestCommandLineRunner
        {
            OnExecute = (cmd, args, wd) => Task.FromResult((1, "Fatal git error"))
        };
        var mockStore = new TestKnowledgeStore();
        var skipper = new SmartTestSkipper(mockRunner, mockStore);

        // Act
        var result = await skipper.GetTestFilterAsync("solution.sln", "C:/repo", CancellationToken.None);

        // Assert
        Assert.Equal("", result);
    }

    [Fact]
    public async Task GetTestFilterAsync_ReturnsEmptyString_WhenNoGraphSnapshotExists()
    {
        // Arrange
        var mockRunner = new TestCommandLineRunner
        {
            OnExecute = (cmd, args, wd) => Task.FromResult((0, " M src/File.cs\n"))
        };
        var mockStore = new TestKnowledgeStore
        {
            OnGetLatestGraphVersion = () => Task.FromResult(0)
        };
        var skipper = new SmartTestSkipper(mockRunner, mockStore);

        // Act
        var result = await skipper.GetTestFilterAsync("solution.sln", "C:/repo", CancellationToken.None);

        // Assert
        Assert.Equal("", result);
    }

    [Fact]
    public async Task GetTestFilterAsync_ReturnsDirectTest_WhenTestFileitselfIsModified()
    {
        // Arrange
        var mockRunner = new TestCommandLineRunner
        {
            OnExecute = (cmd, args, wd) => Task.FromResult((0, " M tests/MyTest.cs\n"))
        };

        var nodes = new List<RichNode>
        {
            new RichNode(1, "tests/MyTest.cs", "MyNamespace", "MyTestClass", "UnitTest", "")
        };
        var edges = new List<RichEdge>();

        var mockStore = new TestKnowledgeStore
        {
            OnGetLatestGraphVersion = () => Task.FromResult(1),
            OnGetSnapshot = (v) => Task.FromResult(new ExtractedKnowledge(nodes, edges))
        };
        var skipper = new SmartTestSkipper(mockRunner, mockStore);

        // Act
        var result = await skipper.GetTestFilterAsync("solution.sln", "C:/repo", CancellationToken.None);

        // Assert
        Assert.Equal("FullyQualifiedName~MyNamespace.MyTestClass", result);
    }

    [Fact]
    public async Task GetTestFilterAsync_ReturnsTests_WhenDependenciesAreModified()
    {
        // Arrange
        var mockRunner = new TestCommandLineRunner
        {
            OnExecute = (cmd, args, wd) => Task.FromResult((0, " M src/Service.cs\n"))
        };

        var nodes = new List<RichNode>
        {
            new RichNode(1, "src/Service.cs", "MyNamespace", "Service", "Class", ""),
            new RichNode(2, "tests/ServiceTests.cs", "MyNamespace.Tests", "ServiceTests", "UnitTest", "")
        };
        // ServiceTests (2) depends on Service (1), so there is an edge from 2 to 1 (2 calls/uses 1)
        var edges = new List<RichEdge>
        {
            new RichEdge(2, 1, "Calls")
        };

        var mockStore = new TestKnowledgeStore
        {
            OnGetLatestGraphVersion = () => Task.FromResult(1),
            OnGetSnapshot = (v) => Task.FromResult(new ExtractedKnowledge(nodes, edges))
        };
        var skipper = new SmartTestSkipper(mockRunner, mockStore);

        // Act
        var result = await skipper.GetTestFilterAsync("solution.sln", "C:/repo", CancellationToken.None);

        // Assert
        Assert.Equal("FullyQualifiedName~MyNamespace.Tests.ServiceTests", result);
    }

    [Fact]
    public async Task GetTestFilterAsync_ReturnsNull_WhenCsFilesChangedButNoTestIsImpacted()
    {
        // Arrange
        var mockRunner = new TestCommandLineRunner
        {
            OnExecute = (cmd, args, wd) => Task.FromResult((0, " M src/Service.cs\n"))
        };

        var nodes = new List<RichNode>
        {
            new RichNode(1, "src/Service.cs", "MyNamespace", "Service", "Class", ""),
            new RichNode(2, "tests/UnrelatedTests.cs", "MyNamespace.Tests", "UnrelatedTests", "UnitTest", "")
        };
        var edges = new List<RichEdge>(); // No connection

        var mockStore = new TestKnowledgeStore
        {
            OnGetLatestGraphVersion = () => Task.FromResult(1),
            OnGetSnapshot = (v) => Task.FromResult(new ExtractedKnowledge(nodes, edges))
        };
        var skipper = new SmartTestSkipper(mockRunner, mockStore);

        // Act
        var result = await skipper.GetTestFilterAsync("solution.sln", "C:/repo", CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetTestFilterAsync_ReturnsEmptyString_WhenMoreThan15TestsAreImpacted()
    {
        // Arrange
        var mockRunner = new TestCommandLineRunner
        {
            OnExecute = (cmd, args, wd) => Task.FromResult((0, " M src/Core.cs\n"))
        };

        var nodes = new List<RichNode> { new RichNode(0, "src/Core.cs", "MyNamespace", "Core", "Class", "") };
        var edges = new List<RichEdge>();

        for (int i = 1; i <= 16; i++)
        {
            nodes.Add(new RichNode(i, $"tests/Test{i}.cs", "MyNamespace.Tests", $"Test{i}", "UnitTest", ""));
            edges.Add(new RichEdge(i, 0, "Calls"));
        }

        var mockStore = new TestKnowledgeStore
        {
            OnGetLatestGraphVersion = () => Task.FromResult(1),
            OnGetSnapshot = (v) => Task.FromResult(new ExtractedKnowledge(nodes, edges))
        };
        var skipper = new SmartTestSkipper(mockRunner, mockStore);

        // Act
        var result = await skipper.GetTestFilterAsync("solution.sln", "C:/repo", CancellationToken.None);

        // Assert
        Assert.Equal("", result);
    }

    [Fact]
    public async Task GetTestFilterAsync_NormalizesPathSlashesCorrectly()
    {
        // Arrange
        var mockRunner = new TestCommandLineRunner
        {
            // Git status outputs relative paths with forward slashes
            OnExecute = (cmd, args, wd) => Task.FromResult((0, " M src/Service.cs\n"))
        };

        var nodes = new List<RichNode>
        {
            // Node has Windows-style backslashes
            new RichNode(1, "src\\Service.cs", "MyNamespace", "Service", "Class", ""),
            new RichNode(2, "tests\\ServiceTests.cs", "MyNamespace.Tests", "ServiceTests", "UnitTest", "")
        };
        var edges = new List<RichEdge>
        {
            new RichEdge(2, 1, "Calls")
        };

        var mockStore = new TestKnowledgeStore
        {
            OnGetLatestGraphVersion = () => Task.FromResult(1),
            OnGetSnapshot = (v) => Task.FromResult(new ExtractedKnowledge(nodes, edges))
        };
        var skipper = new SmartTestSkipper(mockRunner, mockStore);

        // Act
        var result = await skipper.GetTestFilterAsync("solution.sln", "C:/repo", CancellationToken.None);

        // Assert
        Assert.Equal("FullyQualifiedName~MyNamespace.Tests.ServiceTests", result);
    }

    [Fact]
    public async Task GetImpactedFrontendTestsAsync_ReturnsFrontendTest_WhenTsFileIsModified()
    {
        // Arrange
        var mockRunner = new TestCommandLineRunner
        {
            OnExecute = (cmd, args, wd) => Task.FromResult((0, " M src/Component.tsx\n"))
        };

        var nodes = new List<RichNode>
        {
            new RichNode(1, "src/Component.tsx", "", "Component", "TypeScriptModule", ""),
            new RichNode(2, "src/Component.test.tsx", "", "Component.test", "UnitTest", "")
        };
        var edges = new List<RichEdge>
        {
            new RichEdge(2, 1, "DependsOn")
        };

        var mockStore = new TestKnowledgeStore
        {
            OnGetLatestGraphVersion = () => Task.FromResult(1),
            OnGetSnapshot = (v) => Task.FromResult(new ExtractedKnowledge(nodes, edges))
        };
        var skipper = new SmartTestSkipper(mockRunner, mockStore);

        // Act
        var result = await skipper.GetImpactedFrontendTestsAsync("C:/repo", CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("src/Component.test.tsx", result[0]);
    }
}
