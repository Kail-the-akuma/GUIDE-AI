using System;
using System.Threading.Tasks;
using Lmp.Domain;
using Lmp.Infrastructure.Persistence;

namespace Lmp.Application;

public class AssignCarrierCommandHandler : IHandler<AssignCarrierCommand, bool>
{
    private readonly IShipmentRepository _shipmentRepository;
    private readonly IOutboxPublisher _outboxPublisher;

    public AssignCarrierCommandHandler(IShipmentRepository shipmentRepository, IOutboxPublisher outboxPublisher)
    {
        _shipmentRepository = shipmentRepository;
        _outboxPublisher = outboxPublisher;
    }

    public async Task<bool> Handle(AssignCarrierCommand command)
    {
        var shipment = await _shipmentRepository.GetByIdAsync(command.ShipmentId);
        if (shipment == null) return false;
        shipment.CarrierId = command.CarrierId;
        shipment.Status = "CarrierAssigned";
        await _shipmentRepository.UpdateAsync(shipment);
        await _outboxPublisher.PublishAsync(new CarrierAssignedEvent(command.ShipmentId, command.CarrierId));
        return true;
    }
}