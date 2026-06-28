using System.Collections.Generic;
using System.Threading.Tasks;
using Guide.Core.Models;

namespace Guide.Core.Interfaces;

public interface IKnowledgeStore
{
    Task SaveGraphSnapshotAsync(int version, ExtractedKnowledge knowledge);
    Task<int> GetLatestGraphVersionAsync();
    Task<ExtractedKnowledge> GetSnapshotAsync(int version);

    // Persistência de fluxos do Feature Graph
    Task MapFeatureFlowAsync(string featureName, IEnumerable<string> relatedNodes);
    Task<IEnumerable<string>> GetFeatureFlowAsync(string featureName);
}
