namespace Lmp.Domain;
public record Money(decimal Amount = 0, string Currency = "USD") : ValueObject;