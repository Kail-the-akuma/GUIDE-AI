using System;

namespace Guide.Benchmarks;

public class ConsoleChartPlotter
{
    public static void PlotBarChart(string label, double controlValue, double expValue, string unit, int maxBlocks = 40)
    {
        Console.WriteLine($"\n--- Comparison: {label} ---");

        double maxVal = Math.Max(controlValue, expValue);
        if (maxVal == 0) maxVal = 1;

        int controlBlocks = (int)((controlValue / maxVal) * maxBlocks);
        int expBlocks = (int)((expValue / maxVal) * maxBlocks);

        // Ensure blocks are within range [0, maxBlocks]
        controlBlocks = Math.Clamp(controlBlocks, 0, maxBlocks);
        expBlocks = Math.Clamp(expBlocks, 0, maxBlocks);

        string controlBar = new string('█', controlBlocks).PadRight(maxBlocks);
        string expBar = new string('█', expBlocks).PadRight(maxBlocks);

        string format = unit == "USD" ? "F4" : "F2";

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Control: [{controlBar}] {controlValue.ToString(format)} {unit}");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"GUIDE  : [{expBar}] {expValue.ToString(format)} {unit}");

        Console.ResetColor();
    }
}
