using System;
namespace Lmp.Domain;
public class Warehouse : Entity
{
    public string Name { get; set; } = string.Empty;
    public Address Location { get; set; } = new();
}