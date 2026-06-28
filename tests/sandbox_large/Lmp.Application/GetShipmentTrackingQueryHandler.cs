using System.Threading.Tasks;
using Lmp.Infrastructure.Persistence;

namespace Lmp.Application;

public class GetShipmentTrackingQueryHandler : IHandler<GetShipmentTrackingQuery, string>
{
    private readonly IShipmentRepository _shipmentRepository;

    public GetShipmentTrackingQueryHandler(IShipmentRepository shipmentRepository)
    {
        _shipmentRepository = shipmentRepository;
    }

    public async Task<string> Handle(GetShipmentTrackingQuery query)
    {
        var shipment = await _shipmentRepository.GetByIdAsync(query.ShipmentId);
        return shipment?.Status ?? "NotFound";
    }
}