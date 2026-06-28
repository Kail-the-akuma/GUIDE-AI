using System.Collections.Generic;

namespace Guide.Core.Models;

public class DependencyGraph
{
    public List<RichNode> Nodes { get; set; } = new();
    public List<RichEdge> Edges { get; set; } = new();
}
