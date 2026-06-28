using System;
using System.Collections.Generic;

namespace Guide.Benchmarks;

public enum LlmModel
{
    Claude35Sonnet,
    Gpt4o,
    Gpt4oMini,
    Gemini15Flash,
    Gemini15Pro
}

public class FinancialCalculator
{
    // Prices per 1 Million Tokens (Input and Output)
    private static readonly Dictionary<LlmModel, (double InputPrice, double OutputPrice)> PriceMatrix = new()
    {
        { LlmModel.Claude35Sonnet, (3.00, 15.00) },
        { LlmModel.Gpt4o, (5.00, 15.00) },
        { LlmModel.Gpt4oMini, (0.150, 0.600) },
        { LlmModel.Gemini15Flash, (0.075, 0.300) },
        { LlmModel.Gemini15Pro, (1.25, 5.00) }
    };

    public static double CalculateCost(LlmModel model, int inputTokens, int outputTokens)
    {
        if (!PriceMatrix.TryGetValue(model, out var pricing))
        {
            throw new ArgumentException("Modelo de LLM desconhecido.");
        }

        double inputCost = (inputTokens / 1_000_000.0) * pricing.InputPrice;
        double outputCost = (outputTokens / 1_000_000.0) * pricing.OutputPrice;

        return inputCost + outputCost;
    }
}
