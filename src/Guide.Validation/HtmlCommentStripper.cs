using System;
using System.Text.RegularExpressions;
using Guide.Core.Interfaces;

namespace Guide.Validation
{
    public class HtmlCommentStripper : ICommentStripper
    {
        private static readonly Regex CommentRegex = new(
            @"<!--[\s\S]*?-->",
            RegexOptions.Compiled);

        public bool CanStrip(string fileExtension)
        {
            if (string.IsNullOrEmpty(fileExtension)) return false;
            var ext = fileExtension.StartsWith(".") ? fileExtension : "." + fileExtension;
            return ext.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".htm", StringComparison.OrdinalIgnoreCase);
        }

        public string StripComments(string code)
        {
            if (string.IsNullOrEmpty(code)) return code;
            return CommentRegex.Replace(code, string.Empty);
        }
    }
}
