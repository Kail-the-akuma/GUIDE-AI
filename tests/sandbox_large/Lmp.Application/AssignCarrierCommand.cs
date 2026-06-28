using System;
namespace Lmp.Application;
public record AssignCarrierCommand(Guid ShipmentId, Guid CarrierId) : ICommand;