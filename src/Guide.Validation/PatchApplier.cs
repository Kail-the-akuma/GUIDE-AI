using System;
using System.Collections.Generic;

namespace Guide.Validation;

public static class PatchApplier
{
    public static string ApplyPatches(string targetContent, string patchText)
    {
        if (targetContent == null)
        {
            throw new ArgumentNullException(nameof(targetContent));
        }

        if (patchText == null)
        {
            throw new ArgumentNullException(nameof(patchText));
        }

        // Detect target line ending
        string targetNewline = targetContent.Contains("\r\n") ? "\r\n" : "\n";

        // Normalize target content to \n for internal processing
        string normalizedTarget = targetContent.Replace("\r\n", "\n").Replace("\r", "\n");

        // Parse patches
        string[] lines = patchText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        List<PatchBlock> patches = new List<PatchBlock>();
        List<string> currentSearchLines = new List<string>();
        List<string> currentReplaceLines = new List<string>();
        int state = 0; // 0 = Idle, 1 = InSearch, 2 = InReplace

        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("<<<<<<< SEARCH"))
            {
                state = 1;
                currentSearchLines.Clear();
                currentReplaceLines.Clear();
            }
            else if (trimmed.StartsWith("======="))
            {
                if (state == 1)
                {
                    state = 2;
                }
            }
            else if (trimmed.StartsWith(">>>>>>> REPLACE"))
            {
                if (state == 2)
                {
                    patches.Add(new PatchBlock
                    {
                        SearchContent = string.Join("\n", currentSearchLines),
                        ReplaceContent = string.Join("\n", currentReplaceLines)
                    });
                    state = 0;
                }
            }
            else
            {
                if (state == 1)
                {
                    currentSearchLines.Add(line);
                }
                else if (state == 2)
                {
                    currentReplaceLines.Add(line);
                }
            }
        }

        if (patches.Count == 0)
        {
            // No patch blocks found, return original content
            return targetContent;
        }

        // Apply all patches
        foreach (PatchBlock patch in patches)
        {
            if (!normalizedTarget.Contains(patch.SearchContent))
            {
                throw new InvalidOperationException($"Search content not found in target file:\n{patch.SearchContent}");
            }
            normalizedTarget = normalizedTarget.Replace(patch.SearchContent, patch.ReplaceContent);
        }

        // Restore target line ending
        if (targetNewline == "\r\n")
        {
            return normalizedTarget.Replace("\n", "\r\n");
        }

        return normalizedTarget;
    }

    private class PatchBlock
    {
        public string SearchContent { get; set; } = string.Empty;
        public string ReplaceContent { get; set; } = string.Empty;
    }
}
