using System.Threading;
using System.Threading.Tasks;

namespace Guide.Validation
{
    #region Core Engine Contract

    public interface IWorkflowEngine
    {
        Task<WorkflowResult> RunWorkflowAsync(string taskDescription, string targetFilePath, CancellationToken ct);
    }

    #endregion
}
