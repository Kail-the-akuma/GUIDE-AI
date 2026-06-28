using System.Collections.Generic;

namespace Guide.Validation
{
    #region Workflow Execution Result Model

    public class WorkflowResult
    {
        public bool IsSuccess { get; set; }
        public List<string> Errors { get; set; } = new();
        public int HealingIterations { get; set; }
    }

    #endregion
}
