using System;
namespace Lmp.Domain;
public class Driver : Entity
{
    public string Name { get; set; } = string.Empty;
    public Guid CarrierId { get; set; }
}