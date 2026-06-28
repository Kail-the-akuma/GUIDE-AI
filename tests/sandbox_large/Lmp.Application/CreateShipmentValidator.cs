using System;
namespace Lmp.Application;
public class CreateShipmentValidator
{
    public bool Validate(CreateShipmentCommand command)
    {
        return command.WarehouseId != Guid.Empty && command.Destination != null;
    }
}