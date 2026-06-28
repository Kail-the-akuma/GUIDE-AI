using System;
using System.Threading.Tasks;
using Lmp.Application;
using Lmp.Domain;

namespace Lmp.Presentation.Api;

public class ShipmentController
{
    private readonly CreateShipmentCommandHandler _createHandler;
    private readonly AssignCarrierCommandHandler _assignHandler;
    private readonly GetShipmentTrackingQueryHandler _trackingHandler;

    public ShipmentController(
        CreateShipmentCommandHandler createHandler,
        AssignCarrierCommandHandler assignHandler,
        GetShipmentTrackingQueryHandler trackingHandler)
    {
        _createHandler = createHandler;
        _assignHandler = assignHandler;
        _trackingHandler = trackingHandler;
    }

    public async Task<Guid> CreateShipment(Guid warehouseId, Address destination, Dimensions size, Weight mass, Money value)
    {
        var command = new CreateShipmentCommand(warehouseId, destination, size, mass, value);
        return await _createHandler.Handle(command);
    }

    public async Task<bool> AssignCarrier(Guid shipmentId, Guid carrierId)
    {
        var command = new AssignCarrierCommand(shipmentId, carrierId);
        return await _assignHandler.Handle(command);
    }

    public async Task<string> GetTrackingStatus(Guid shipmentId)
    {
        var query = new GetShipmentTrackingQuery(shipmentId);
        return await _trackingHandler.Handle(query);
    }
}