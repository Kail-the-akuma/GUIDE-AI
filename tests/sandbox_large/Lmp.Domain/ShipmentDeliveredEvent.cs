using System;
namespace Lmp.Domain;
public record ShipmentDeliveredEvent(Guid ShipmentId, DateTime DeliveredAt);