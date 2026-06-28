using System;
using System.Threading.Tasks;
using Lmp.Infrastructure.Persistence;

namespace Lmp.Presentation.Api;

public class WarehouseController
{
    private readonly IWarehouseRepository _warehouseRepository;

    public WarehouseController(IWarehouseRepository warehouseRepository)
    {
        _warehouseRepository = warehouseRepository;
    }

    public async Task<string> GetWarehouseName(Guid id)
    {
        var wh = await _warehouseRepository.GetByIdAsync(id);
        return wh?.Name ?? "Unknown";
    }
}