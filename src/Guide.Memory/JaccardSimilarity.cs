using System;
using System.Collections.Generic;
using System.Text;

namespace Guide.Memory;

public static class JaccardSimilarity
{
    public static double Compute(string s1, string s2)
    {
        if (s1 == null || s2 == null)
        {
            return 0.0;
        }

        var set1 = Tokenize(s1);
        var set2 = Tokenize(s2);

        if (set1.Count == 0 && set2.Count == 0)
        {
            return 1.0;
        }

        if (set1.Count == 0 || set2.Count == 0)
        {
            return 0.0;
        }

        int intersectionCount = 0;
        foreach (var token in set1)
        {
            if (set2.Contains(token))
            {
                intersectionCount++;
            }
        }

        int unionCount = set1.Count + set2.Count - intersectionCount;

        return (double)intersectionCount / unionCount;
    }

    public static HashSet<string> Tokenize(string input)
    {
        var tokens = new HashSet<string>();
        if (string.IsNullOrEmpty(input))
        {
            return tokens;
        }

        string lowerInput = input.ToLowerInvariant();

        // 1. Lowercase words: splitting by spaces and punctuation
        var currentWord = new StringBuilder();
        for (int i = 0; i < lowerInput.Length; i++)
        {
            char c = lowerInput[i];
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c))
            {
                if (currentWord.Length > 0)
                {
                    tokens.Add(currentWord.ToString());
                    currentWord.Clear();
                }
            }
            else
            {
                currentWord.Append(c);
            }
        }
        if (currentWord.Length > 0)
        {
            tokens.Add(currentWord.ToString());
        }

        // 2. Character trigrams (3-grams) from the lowercased input
        for (int i = 0; i <= lowerInput.Length - 3; i++)
        {
            tokens.Add(lowerInput.Substring(i, 3));
        }

        return tokens;
    }
}
