using System;
using System.Threading.Tasks;
using Lmp.Domain;
using Lmp.Application;
using Lmp.Infrastructure.Persistence;
using Xunit;

namespace Lmp.Tests;

public class CreateShipmentCommandHandlerTests
{
    private class FakeShipmentRepository : IShipmentRepository
    {
        public Shipment? Shipment { get; private set; }
        public Task<Shipment?> GetByIdAsync(Guid id) => Task.FromResult(Shipment);
        public Task AddAsync(Shipment shipment) { Shipment = shipment; return Task.CompletedTask; }
        public Task UpdateAsync(Shipment shipment) { Shipment = shipment; return Task.CompletedTask; }
    }

    private class FakeWarehouseService : IWarehouseService
    {
        public Task<bool> ValidateWarehouseAsync(Guid warehouseId) => Task.FromResult(warehouseId != Guid.Empty);
    }

    private class FakeOutboxPublisher : IOutboxPublisher
    {
        public bool Published { get; private set; }
        public Task PublishAsync<TEvent>(TEvent domainEvent) { Published = true; return Task.CompletedTask; }
    }

    [Fact]
    public async Task TestCreateShipment()
    {
        var repo = new FakeShipmentRepository();
        var whService = new FakeWarehouseService();
        var publisher = new FakeOutboxPublisher();
        var handler = new CreateShipmentCommandHandler(repo, whService, publisher);

        var warehouseId = Guid.NewGuid();
        var command = new CreateShipmentCommand(
            warehouseId,
            new Address("123 Main St", "Metropolis", "12345"),
            new Dimensions(10, 10, 10),
            new Weight(5, "kg"),
            new Money(100, "USD")
        );

        var shipmentId = await handler.Handle(command);

        Assert.NotEqual(Guid.Empty, shipmentId);
        Assert.NotNull(repo.Shipment);
        Assert.Equal(warehouseId, repo.Shipment.WarehouseId);
        Assert.True(publisher.Published);
    }
}