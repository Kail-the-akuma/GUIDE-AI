using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lmp.Domain;

namespace Lmp.Infrastructure.Persistence;

public class ShipmentRepository : IShipmentRepository
{
    private readonly Dictionary<Guid, Shipment> _shipments = new();

    public Task<Shipment?> GetByIdAsync(Guid id)
    {
        _shipments.TryGetValue(id, out var shipment);
        return Task.FromResult(shipment);
    }

    public Task AddAsync(Shipment shipment)
    {
        _shipments[shipment.Id] = shipment;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Shipment shipment)
    {
        _shipments[shipment.Id] = shipment;
        return Task.CompletedTask;
    }
}