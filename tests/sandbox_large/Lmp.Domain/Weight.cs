namespace Lmp.Domain;
public record Weight(double Value = 0, string Unit = "kg") : ValueObject;