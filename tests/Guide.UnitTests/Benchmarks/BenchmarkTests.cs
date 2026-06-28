using System;
using Xunit;
using Guide.Benchmarks;

namespace Guide.UnitTests.Benchmarks;

public class BenchmarkTests
{
    [Fact]
    public void CalculateCost_DeveCalcularCustoCorreto_QuandoModeloValido()
    {
        // Arrange
        var model = LlmModel.Claude35Sonnet;
        int inputTokens = 1_000_000;
        int outputTokens = 1_000_000;

        // Act
        double cost = FinancialCalculator.CalculateCost(model, inputTokens, outputTokens);

        // Assert
        Assert.Equal(18.00, cost);
    }

    [Fact]
    public void CalculateCost_DeveLancarArgumentException_QuandoModeloInvalido()
    {
        // Arrange
        var invalidModel = (LlmModel)999;

        // Act & Assert
        Assert.Throws<ArgumentException>(() => FinancialCalculator.CalculateCost(invalidModel, 100, 100));
    }

    [Fact]
    public void Mutate_DeveInjetarErroSintatico_QuandoTipoForSyntaxError()
    {
        // Arrange
        var mutator = new RoslynCodeMutator("SyntaxError");
        string source = @"
namespace BankAccountSystem;
public class AccountService
{
    public void Deposit(string accountNumber, decimal amount)
    {
        Console.WriteLine(""Deposit logic"");
    }
}";

        // Act
        string mutated = mutator.Mutate(source);

        // Assert
        Assert.Contains("Console.WriteLine(\"Missing semicolon\")", mutated);
        Assert.DoesNotContain("Console.WriteLine(\"Missing semicolon\");", mutated);
    }

    [Fact]
    public void Mutate_DeveInjetarDrift_QuandoTipoForArchitectureDrift()
    {
        // Arrange
        var mutator = new RoslynCodeMutator("ArchitectureDrift");
        string source = @"
namespace BankAccountSystem;
public class AccountService
{
    public void Deposit(string accountNumber, decimal amount)
    {
        Console.WriteLine(""Deposit logic"");
    }
}";

        // Act
        string mutated = mutator.Mutate(source);

        // Assert
        Assert.Contains("Lmp.Presentation.Api.ShipmentController", mutated);
    }
}
