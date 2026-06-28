using System.Collections.Generic;

namespace Guide.Core.Models;

public record NodeInfo(
    int? Id,
    string FilePath,
    string Namespace,
    string Name,
    string NodeType,
    string PublicSignatures
);

public record RichNode(
    int? Id,
    string FilePath,
    string Namespace,
    string Name,
    string NodeType,
    string PublicSignatures,
    double Confidence = 1.0,
    string DetectionMethod = "Manual",
    Dictionary<string, string>? Properties = null
)
{
    public Dictionary<string, string> Properties { get; init; } = Properties ?? new Dictionary<string, string>();
}

public record RichEdge(
    int? FromNodeId,
    int? ToNodeId,
    string RelationType,
    double Confidence = 1.0,
    string DetectionMethod = "Manual",
    Dictionary<string, string>? Properties = null
)
{
    public Dictionary<string, string> Properties { get; init; } = Properties ?? new Dictionary<string, string>();
}

public record ExtractedKnowledge(
    IEnumerable<RichNode> Nodes,
    IEnumerable<RichEdge> Edges
);
