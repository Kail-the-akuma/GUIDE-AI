using System.Collections.Generic;
using System.Threading.Tasks;
using Guide.Core.Models;

namespace Guide.Core.Interfaces;

public interface IEngineeringKnowledge
{
    Task SyncLocalDocsAsync(string knowledgeDirectoryPath);
    Task RecordEntryAsync(string context, string ruleOrPitfall);
    Task<IEnumerable<KnowledgeEntry>> QueryKnowledgeAsync(string userIntent);
}
