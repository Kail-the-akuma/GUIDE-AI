using System;
using Guide.Core.Interfaces;

namespace Guide.Validation
{
    public class CSharpCommentStripper : ICommentStripper
    {
        public bool CanStrip(string fileExtension)
        {
            if (string.IsNullOrEmpty(fileExtension)) return false;
            var ext = fileExtension.StartsWith(".") ? fileExtension : "." + fileExtension;
            return ext.Equals(".cs", StringComparison.OrdinalIgnoreCase);
        }

        public string StripComments(string code)
        {
            return SemanticCompactor.CompactCode(code);
        }
    }
}
