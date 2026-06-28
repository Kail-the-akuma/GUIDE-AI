using System;
namespace Lmp.Application;
public record GetShipmentTrackingQuery(Guid ShipmentId) : IQuery<string>;