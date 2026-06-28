using System.Collections.Generic;
using Guide.Core.Models;
using Xunit;

namespace Guide.UnitTests.Core;

public class NodeInfoTests
{
    [Fact]
    public void RichNode_DefaultConstructor_SetsDefaultValues()
    {
        // Act
        var node = new RichNode(
            Id: 1,
            FilePath: "test.cs",
            Namespace: "TestNamespace",
            Name: "TestNode",
            NodeType: "Class",
            PublicSignatures: "public class TestNode"
        );

        // Assert
        Assert.Equal(1.0, node.Confidence);
        Assert.Equal("Manual", node.DetectionMethod);
        Assert.NotNull(node.Properties);
        Assert.Empty(node.Properties);
    }

    [Fact]
    public void RichNode_CustomConstructor_PreservesValues()
    {
        // Arrange
        var customProperties = new Dictionary<string, string> { { "key", "value" } };

        // Act
        var node = new RichNode(
            Id: 2,
            FilePath: "test.cs",
            Namespace: "TestNamespace",
            Name: "TestNode",
            NodeType: "Class",
            PublicSignatures: "public class TestNode",
            Confidence: 0.85,
            DetectionMethod: "AttributeBasedClassifier",
            Properties: customProperties
        );

        // Assert
        Assert.Equal(0.85, node.Confidence);
        Assert.Equal("AttributeBasedClassifier", node.DetectionMethod);
        Assert.NotNull(node.Properties);
        Assert.Single(node.Properties);
        Assert.Equal("value", node.Properties["key"]);
    }

    [Fact]
    public void RichNode_NullProperties_DefaultsToEmptyDictionary()
    {
        // Act
        var node = new RichNode(
            Id: 3,
            FilePath: "test.cs",
            Namespace: "TestNamespace",
            Name: "TestNode",
            NodeType: "Class",
            PublicSignatures: "public class TestNode",
            Confidence: 0.9,
            DetectionMethod: "Analyzer",
            Properties: null
        );

        // Assert
        Assert.NotNull(node.Properties);
        Assert.Empty(node.Properties);
    }

    [Fact]
    public void RichEdge_DefaultConstructor_SetsDefaultValues()
    {
        // Act
        var edge = new RichEdge(
            FromNodeId: 1,
            ToNodeId: 2,
            RelationType: "DependsOn"
        );

        // Assert
        Assert.Equal(1.0, edge.Confidence);
        Assert.Equal("Manual", edge.DetectionMethod);
        Assert.NotNull(edge.Properties);
        Assert.Empty(edge.Properties);
    }

    [Fact]
    public void RichEdge_CustomConstructor_PreservesValues()
    {
        // Arrange
        var customProperties = new Dictionary<string, string> { { "rel", "type" } };

        // Act
        var edge = new RichEdge(
            FromNodeId: 1,
            ToNodeId: 2,
            RelationType: "DependsOn",
            Confidence: 0.95,
            DetectionMethod: "CallGraph",
            Properties: customProperties
        );

        // Assert
        Assert.Equal(0.95, edge.Confidence);
        Assert.Equal("CallGraph", edge.DetectionMethod);
        Assert.NotNull(edge.Properties);
        Assert.Single(edge.Properties);
        Assert.Equal("type", edge.Properties["rel"]);
    }

    [Fact]
    public void RichEdge_NullProperties_DefaultsToEmptyDictionary()
    {
        // Act
        var edge = new RichEdge(
            FromNodeId: 1,
            ToNodeId: 2,
            RelationType: "DependsOn",
            Confidence: 0.7,
            DetectionMethod: "Heuristic",
            Properties: null
        );

        // Assert
        Assert.NotNull(edge.Properties);
        Assert.Empty(edge.Properties);
    }
}
