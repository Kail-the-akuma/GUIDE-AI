using System;
namespace Lmp.Domain;
public record CarrierAssignedEvent(Guid ShipmentId, Guid CarrierId);