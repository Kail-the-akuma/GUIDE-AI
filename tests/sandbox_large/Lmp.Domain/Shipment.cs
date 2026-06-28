using System;
namespace Lmp.Domain;
public class Shipment : Entity
{
    public string TrackingNumber { get; set; } = string.Empty;
    public Guid WarehouseId { get; set; }
    public Guid? CarrierId { get; set; }
    public Address Destination { get; set; } = new();
    public Dimensions Size { get; set; } = new();
    public Weight Mass { get; set; } = new();
    public Money Value { get; set; } = new();
    public string Status { get; set; } = "Created";
}