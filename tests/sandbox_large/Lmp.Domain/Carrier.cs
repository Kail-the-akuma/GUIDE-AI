using System;
namespace Lmp.Domain;
public class Carrier : Entity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}