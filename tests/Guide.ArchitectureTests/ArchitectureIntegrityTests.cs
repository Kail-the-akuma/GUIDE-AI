using NetArchTest.Rules;
using Xunit;

namespace Guide.ArchitectureTests;

public class ArchitectureIntegrityTests
{
    [Fact]
    public void SemanticEngine_ShouldNot_Reference_Validators_Or_Memory_Or_Knowledge()
    {
        var result = Types.InAssembly(typeof(Guide.Semantic.ProjectParser).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Guide.Validation", "Guide.Memory", "Guide.Knowledge")
            .GetResult();

        Assert.True(result.IsSuccessful, "Violação de acoplamento: SemanticEngine refere-se a Validators, Memory ou Knowledge!");
    }

    [Fact]
    public void Core_ShouldNot_Reference_AnyOtherSubproject()
    {
        var result = Types.InAssembly(typeof(Guide.Core.Interfaces.IValidator).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Guide.Semantic", "Guide.Validation", "Guide.Knowledge", "Guide.Memory")
            .GetResult();

        Assert.True(result.IsSuccessful, "Violação de acoplamento: Core refere-se a outros subprojectos!");
    }

    [Fact]
    public void Validators_ShouldNot_Reference_Knowledge_Or_Memory()
    {
        var result = Types.InAssembly(typeof(Guide.Validation.ParallelValidator).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Guide.Knowledge", "Guide.Memory")
            .GetResult();

        Assert.True(result.IsSuccessful, "Violação de acoplamento: Validators refere-se a Knowledge ou Memory!");
    }

    [Fact]
    public void Knowledge_ShouldNot_Reference_Validators_Or_Memory()
    {
        var result = Types.InAssembly(typeof(Guide.Knowledge.KnowledgeStore).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Guide.Validation", "Guide.Memory")
            .GetResult();

        Assert.True(result.IsSuccessful, "Violação de acoplamento: Knowledge refere-se a Validators ou Memory!");
    }

    [Fact]
    public void Memory_ShouldNot_Reference_SemanticEngine_Or_Validators_Or_Knowledge()
    {
        var result = Types.InAssembly(typeof(Guide.Memory.EngineeringMemoryStore).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Guide.Semantic", "Guide.Validation", "Guide.Knowledge")
            .GetResult();

        Assert.True(result.IsSuccessful, "Violação de acoplamento: Memory refere-se a SemanticEngine, Validators ou Knowledge!");
    }
}
