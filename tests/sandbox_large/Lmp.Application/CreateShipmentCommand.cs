using System;
using Lmp.Domain;
namespace Lmp.Application;
public record CreateShipmentCommand(Guid WarehouseId, Address Destination, Dimensions Size, Weight Mass, Money Value) : ICommand;