using System;
using System.Threading.Tasks;
using Lmp.Domain;

namespace Lmp.Application
{
    public interface IWarehouseService
    {
        Task<bool> ValidateWarehouseAsync(Guid warehouseId);
    }
}

namespace Lmp.Infrastructure.Persistence
{
    public interface IShipmentRepository
    {
        Task<Shipment?> GetByIdAsync(Guid id);
        Task AddAsync(Shipment shipment);
        Task UpdateAsync(Shipment shipment);
    }

    public interface IWarehouseRepository
    {
        Task<Warehouse?> GetByIdAsync(Guid id);
        Task AddAsync(Warehouse warehouse);
    }
}