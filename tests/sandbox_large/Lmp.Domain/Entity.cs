using System;
namespace Lmp.Domain;
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
}