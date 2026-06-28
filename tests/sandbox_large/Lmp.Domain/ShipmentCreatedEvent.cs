using System;
namespace Lmp.Domain;
public record ShipmentCreatedEvent(Guid ShipmentId, string TrackingNumber);