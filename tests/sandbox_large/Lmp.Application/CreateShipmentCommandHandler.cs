using System;
using System.Threading.Tasks;
using Lmp.Domain;
using Lmp.Infrastructure.Persistence;

namespace Lmp.Application;

public class CreateShipmentCommandHandler : IHandler<CreateShipmentCommand, Guid>
{
    private readonly IShipmentRepository _shipmentRepository;
    private readonly IWarehouseService _warehouseService;
    private readonly IOutboxPublisher _outboxPublisher;

    public CreateShipmentCommandHandler(
        IShipmentRepository shipmentRepository,
        IWarehouseService warehouseService,
        IOutboxPublisher outboxPublisher)
    {
        _shipmentRepository = shipmentRepository;
        _warehouseService = warehouseService;
        _outboxPublisher = outboxPublisher;
    }

    public async Task<Guid> Handle(CreateShipmentCommand command)
    {
        var isValid = await _warehouseService.ValidateWarehouseAsync(command.WarehouseId);
        if (!isValid)
        {
            throw new ArgumentException("Invalid warehouse");
        }

        var shipment = new Shipment
        {
            Id = Guid.NewGuid(),
            TrackingNumber = "TRK-" + Guid.NewGuid().ToString().Substring(0, 8).ToUpper(),
            WarehouseId = command.WarehouseId,
            Destination = command.Destination,
            Size = command.Size,
            Mass = command.Mass,
            Value = command.Value,
            Status = "Created"
        };

        await _shipmentRepository.AddAsync(shipment);
        
        await _outboxPublisher.PublishAsync(new ShipmentCreatedEvent(shipment.Id, shipment.TrackingNumber));

        return shipment.Id;
    }
}