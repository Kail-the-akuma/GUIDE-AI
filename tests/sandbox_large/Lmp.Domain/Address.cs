namespace Lmp.Domain;
public record Address(string Street = "", string City = "", string ZipCode = "") : ValueObject;