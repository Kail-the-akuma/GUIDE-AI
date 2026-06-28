using System;
using System.Text.RegularExpressions;
using Guide.Core.Interfaces;

namespace Guide.Validation
{
    public class TypeScriptCommentStripper : ICommentStripper
    {
        private static readonly Regex CommentRegex = new(
            @"(@""[^""]*""|""(\\.|[^""\\])*""|'(\\.|[^'\\])*'|`(\\.|[^`\\])*`)|(/\*[\s\S]*?\*/|//.*)",
            RegexOptions.Compiled);

        public bool CanStrip(string fileExtension)
        {
            if (string.IsNullOrEmpty(fileExtension)) return false;
            var ext = fileExtension.StartsWith(".") ? fileExtension : "." + fileExtension;
            return ext.Equals(".ts", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".tsx", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".js", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".jsx", StringComparison.OrdinalIgnoreCase);
        }

        public string StripComments(string code)
        {
            if (string.IsNullOrEmpty(code)) return code;
            return CommentRegex.Replace(code, me =>
            {
                if (me.Groups[1].Success)
                {
                    return me.Groups[1].Value;
                }
                return string.Empty;
            });
        }
    }
}
