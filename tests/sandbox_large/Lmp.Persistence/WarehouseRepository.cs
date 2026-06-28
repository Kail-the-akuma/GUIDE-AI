using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lmp.Domain;
using Lmp.Application;

namespace Lmp.Infrastructure.Persistence;

public class WarehouseRepository : IWarehouseRepository, IWarehouseService
{
    private readonly Dictionary<Guid, Warehouse> _warehouses = new();

    public Task<Warehouse?> GetByIdAsync(Guid id)
    {
        _warehouses.TryGetValue(id, out var warehouse);
        return Task.FromResult(warehouse);
    }

    public Task AddAsync(Warehouse warehouse)
    {
        _warehouses[warehouse.Id] = warehouse;
        return Task.CompletedTask;
    }

    public Task<bool> ValidateWarehouseAsync(Guid warehouseId)
    {
        return Task.FromResult(true);
    }
}