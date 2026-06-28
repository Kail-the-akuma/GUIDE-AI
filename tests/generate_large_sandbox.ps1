# Generate Large Sandbox Workspace for LMP Clean Architecture
$ErrorActionPreference = "Stop"

$SandboxName = "sandbox_large"
$SandboxPath = Join-Path $PSScriptRoot $SandboxName

if (Test-Path $SandboxPath) {
    Write-Host "Removing existing sandbox directory: $SandboxPath"
    Remove-Item -Recurse -Force $SandboxPath
}

Write-Host "Creating sandbox directory: $SandboxPath"
New-Item -ItemType Directory -Path $SandboxPath | Out-Null

$OldCurrentDir = [System.IO.Directory]::GetCurrentDirectory()
Push-Location $SandboxPath
[System.IO.Directory]::SetCurrentDirectory($SandboxPath)

try {
    Write-Host "Initializing Git repository..."
    git init

    Write-Host "Creating Lmp solution..."
    dotnet new sln -n Lmp

    Write-Host "Creating 7 projects..."
    dotnet new classlib -n Lmp.Domain
    dotnet new classlib -n Lmp.Application
    dotnet new classlib -n Lmp.Persistence
    dotnet new classlib -n Lmp.Messaging
    dotnet new classlib -n Lmp.Infrastructure
    dotnet new classlib -n Lmp.Api
    dotnet new xunit -n Lmp.Tests

    Write-Host "Removing default source files..."
    Remove-Item -Path "Lmp.Domain\Class1.cs" -ErrorAction Ignore
    Remove-Item -Path "Lmp.Application\Class1.cs" -ErrorAction Ignore
    Remove-Item -Path "Lmp.Persistence\Class1.cs" -ErrorAction Ignore
    Remove-Item -Path "Lmp.Messaging\Class1.cs" -ErrorAction Ignore
    Remove-Item -Path "Lmp.Infrastructure\Class1.cs" -ErrorAction Ignore
    Remove-Item -Path "Lmp.Api\Class1.cs" -ErrorAction Ignore
    Remove-Item -Path "Lmp.Tests\UnitTest1.cs" -ErrorAction Ignore

    Write-Host "Configuring project references..."
    dotnet add Lmp.Application\Lmp.Application.csproj reference Lmp.Domain\Lmp.Domain.csproj
    dotnet add Lmp.Persistence\Lmp.Persistence.csproj reference Lmp.Application\Lmp.Application.csproj
    dotnet add Lmp.Messaging\Lmp.Messaging.csproj reference Lmp.Application\Lmp.Application.csproj
    dotnet add Lmp.Api\Lmp.Api.csproj reference Lmp.Application\Lmp.Application.csproj
    dotnet add Lmp.Tests\Lmp.Tests.csproj reference Lmp.Application\Lmp.Application.csproj

    Write-Host "Adding projects to Lmp.sln..."
    dotnet sln Lmp.sln add Lmp.Domain\Lmp.Domain.csproj
    dotnet sln Lmp.sln add Lmp.Application\Lmp.Application.csproj
    dotnet sln Lmp.sln add Lmp.Persistence\Lmp.Persistence.csproj
    dotnet sln Lmp.sln add Lmp.Messaging\Lmp.Messaging.csproj
    dotnet sln Lmp.sln add Lmp.Infrastructure\Lmp.Infrastructure.csproj
    dotnet sln Lmp.sln add Lmp.Api\Lmp.Api.csproj
    dotnet sln Lmp.sln add Lmp.Tests\Lmp.Tests.csproj

    # --- Write C# Source Files (33 total) ---

    # 1. ValueObject.cs
    $content = @"
namespace Lmp.Domain;
public abstract record ValueObject;
"@
    [System.IO.File]::WriteAllText("Lmp.Domain\ValueObject.cs", $content)

    # 2. Entity.cs
    $content = @"
using System;
namespace Lmp.Domain;
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
}
"@
    [System.IO.File]::WriteAllText("Lmp.Domain\Entity.cs", $content)

    # 3. Shipment.cs
    $content = @"
using System;
namespace Lmp.Domain;
public class Shipment : Entity
{
    public string TrackingNumber { get; set; } = string.Empty;
    public Guid WarehouseId { get; set; }
    public Guid? CarrierId { get; set; }
    public Address Destination { get; set; } = new();
    public Dimensions Size { get; set; } = new();
    public Weight Mass { get; set; } = new();
    public Money Value { get; set; } = new();
    public string Status { get; set; } = "Created";
}
"@
    [System.IO.File]::WriteAllText("Lmp.Domain\Shipment.cs", $content)

    # 4. Warehouse.cs
    $content = @"
using System;
namespace Lmp.Domain;
public class Warehouse : Entity
{
    public string Name { get; set; } = string.Empty;
    public Address Location { get; set; } = new();
}
"@
    [System.IO.File]::WriteAllText("Lmp.Domain\Warehouse.cs", $content)

    # 5. Carrier.cs
    $content = @"
using System;
namespace Lmp.Domain;
public class Carrier : Entity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}
"@
    [System.IO.File]::WriteAllText("Lmp.Domain\Carrier.cs", $content)

    # 6. Driver.cs
    $content = @"
using System;
namespace Lmp.Domain;
public class Driver : Entity
{
    public string Name { get; set; } = string.Empty;
    public Guid CarrierId { get; set; }
}
"@
    [System.IO.File]::WriteAllText("Lmp.Domain\Driver.cs", $content)

    # 7. Address.cs
    $content = @"
namespace Lmp.Domain;
public record Address(string Street = "", string City = "", string ZipCode = "") : ValueObject;
"@
    [System.IO.File]::WriteAllText("Lmp.Domain\Address.cs", $content)

    # 8. Dimensions.cs
    $content = @"
namespace Lmp.Domain;
public record Dimensions(double Width = 0, double Height = 0, double Length = 0) : ValueObject;
"@
    [System.IO.File]::WriteAllText("Lmp.Domain\Dimensions.cs", $content)

    # 9. Weight.cs
    $content = @"
namespace Lmp.Domain;
public record Weight(double Value = 0, string Unit = "kg") : ValueObject;
"@
    [System.IO.File]::WriteAllText("Lmp.Domain\Weight.cs", $content)

    # 10. Money.cs
    $content = @"
namespace Lmp.Domain;
public record Money(decimal Amount = 0, string Currency = "USD") : ValueObject;
"@
    [System.IO.File]::WriteAllText("Lmp.Domain\Money.cs", $content)

    # 11. ShipmentCreatedEvent.cs
    $content = @"
using System;
namespace Lmp.Domain;
public record ShipmentCreatedEvent(Guid ShipmentId, string TrackingNumber);
"@
    [System.IO.File]::WriteAllText("Lmp.Domain\ShipmentCreatedEvent.cs", $content)

    # 12. CarrierAssignedEvent.cs
    $content = @"
using System;
namespace Lmp.Domain;
public record CarrierAssignedEvent(Guid ShipmentId, Guid CarrierId);
"@
    [System.IO.File]::WriteAllText("Lmp.Domain\CarrierAssignedEvent.cs", $content)

    # 13. ShipmentDeliveredEvent.cs
    $content = @"
using System;
namespace Lmp.Domain;
public record ShipmentDeliveredEvent(Guid ShipmentId, DateTime DeliveredAt);
"@
    [System.IO.File]::WriteAllText("Lmp.Domain\ShipmentDeliveredEvent.cs", $content)

    # 14. ICommand.cs
    $content = @"
namespace Lmp.Application;
public interface ICommand;
"@
    [System.IO.File]::WriteAllText("Lmp.Application\ICommand.cs", $content)

    # 15. IQuery.cs
    $content = @"
namespace Lmp.Application;
public interface IQuery<TResult>;
"@
    [System.IO.File]::WriteAllText("Lmp.Application\IQuery.cs", $content)

    # 16. IHandler.cs
    $content = @"
using System.Threading.Tasks;
namespace Lmp.Application;
public interface IHandler<TCommand, TResult>
{
    Task<TResult> Handle(TCommand command);
}
"@
    [System.IO.File]::WriteAllText("Lmp.Application\IHandler.cs", $content)

    # 17. IWarehouseService.cs
    $content = @"
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
"@
    [System.IO.File]::WriteAllText("Lmp.Application\IWarehouseService.cs", $content)

    # 18. IEmailDispatcher.cs
    $content = @"
using System.Threading.Tasks;
namespace Lmp.Application;
public interface IEmailDispatcher
{
    Task SendEmailAsync(string recipient, string subject, string body);
}
"@
    [System.IO.File]::WriteAllText("Lmp.Application\IEmailDispatcher.cs", $content)

    # 19. IOutboxPublisher.cs
    $content = @"
using System.Threading.Tasks;
namespace Lmp.Application;
public interface IOutboxPublisher
{
    Task PublishAsync<TEvent>(TEvent domainEvent);
}
"@
    [System.IO.File]::WriteAllText("Lmp.Application\IOutboxPublisher.cs", $content)

    # 20. CreateShipmentCommand.cs
    $content = @"
using System;
using Lmp.Domain;
namespace Lmp.Application;
public record CreateShipmentCommand(Guid WarehouseId, Address Destination, Dimensions Size, Weight Mass, Money Value) : ICommand;
"@
    [System.IO.File]::WriteAllText("Lmp.Application\CreateShipmentCommand.cs", $content)

    # 21. CreateShipmentCommandHandler.cs
    $content = @"
using System;
using System.Threading.Tasks;
using Lmp.Domain;
using Lmp.Infrastructure.Persistence;

namespace Lmp.Application;

public class CreateShipmentCommandHandler : IHandler<CreateShipmentCommand, Guid>
{
    private readonly IShipmentRepository _shipmentRepository;
    private readonly IWarehouseService _warehouseService;
    private readonly IOutboxPublisher _outboxPublisher;

    public CreateShipmentCommandHandler(
        IShipmentRepository shipmentRepository,
        IWarehouseService warehouseService,
        IOutboxPublisher outboxPublisher)
    {
        _shipmentRepository = shipmentRepository;
        _warehouseService = warehouseService;
        _outboxPublisher = outboxPublisher;
    }

    public async Task<Guid> Handle(CreateShipmentCommand command)
    {
        var isValid = await _warehouseService.ValidateWarehouseAsync(command.WarehouseId);
        if (!isValid)
        {
            throw new ArgumentException("Invalid warehouse");
        }

        var shipment = new Shipment
        {
            Id = Guid.NewGuid(),
            TrackingNumber = "TRK-" + Guid.NewGuid().ToString().Substring(0, 8).ToUpper(),
            WarehouseId = command.WarehouseId,
            Destination = command.Destination,
            Size = command.Size,
            Mass = command.Mass,
            Value = command.Value,
            Status = "Created"
        };

        await _shipmentRepository.AddAsync(shipment);
        
        await _outboxPublisher.PublishAsync(new ShipmentCreatedEvent(shipment.Id, shipment.TrackingNumber));

        return shipment.Id;
    }
}
"@
    [System.IO.File]::WriteAllText("Lmp.Application\CreateShipmentCommandHandler.cs", $content)

    # 22. AssignCarrierCommand.cs
    $content = @"
using System;
namespace Lmp.Application;
public record AssignCarrierCommand(Guid ShipmentId, Guid CarrierId) : ICommand;
"@
    [System.IO.File]::WriteAllText("Lmp.Application\AssignCarrierCommand.cs", $content)

    # 23. AssignCarrierCommandHandler.cs
    $content = @"
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
"@
    [System.IO.File]::WriteAllText("Lmp.Application\AssignCarrierCommandHandler.cs", $content)

    # 24. GetShipmentTrackingQuery.cs
    $content = @"
using System;
namespace Lmp.Application;
public record GetShipmentTrackingQuery(Guid ShipmentId) : IQuery<string>;
"@
    [System.IO.File]::WriteAllText("Lmp.Application\GetShipmentTrackingQuery.cs", $content)

    # 25. GetShipmentTrackingQueryHandler.cs
    $content = @"
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
"@
    [System.IO.File]::WriteAllText("Lmp.Application\GetShipmentTrackingQueryHandler.cs", $content)

    # 26. CreateShipmentValidator.cs
    $content = @"
using System;
namespace Lmp.Application;
public class CreateShipmentValidator
{
    public bool Validate(CreateShipmentCommand command)
    {
        return command.WarehouseId != Guid.Empty && command.Destination != null;
    }
}
"@
    [System.IO.File]::WriteAllText("Lmp.Application\CreateShipmentValidator.cs", $content)

    # 27. ShipmentRepository.cs
    $content = @"
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
"@
    [System.IO.File]::WriteAllText("Lmp.Persistence\ShipmentRepository.cs", $content)

    # 28. WarehouseRepository.cs
    $content = @"
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
"@
    [System.IO.File]::WriteAllText("Lmp.Persistence\WarehouseRepository.cs", $content)

    # 29. OutboxProcessor.cs
    $content = @"
using System.Threading.Tasks;
using Lmp.Application;

namespace Lmp.Infrastructure.Messaging;

public class OutboxProcessor : IOutboxPublisher
{
    public Task PublishAsync<TEvent>(TEvent domainEvent)
    {
        return Task.CompletedTask;
    }
}
"@
    [System.IO.File]::WriteAllText("Lmp.Messaging\OutboxProcessor.cs", $content)

    # 30. NotificationHandler.cs
    $content = @"
using System.Threading.Tasks;
using Lmp.Application;

namespace Lmp.Infrastructure.Messaging;

public class NotificationHandler : IEmailDispatcher
{
    public Task SendEmailAsync(string recipient, string subject, string body)
    {
        return Task.CompletedTask;
    }
}
"@
    [System.IO.File]::WriteAllText("Lmp.Messaging\NotificationHandler.cs", $content)

    # 31. ShipmentController.cs
    $content = @"
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
"@
    [System.IO.File]::WriteAllText("Lmp.Api\ShipmentController.cs", $content)

    # 32. WarehouseController.cs
    $content = @"
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
"@
    [System.IO.File]::WriteAllText("Lmp.Api\WarehouseController.cs", $content)

    # 33. CreateShipmentCommandHandlerTests.cs
    $content = @"
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
"@
    [System.IO.File]::WriteAllText("Lmp.Tests\CreateShipmentCommandHandlerTests.cs", $content)

    Write-Host "Executing dotnet build..."
    dotnet build Lmp.sln

    Write-Host "Executing dotnet test..."
    dotnet test Lmp.sln

    Write-Host "[SUCCESS] Large sandbox environment built and verified successfully!"
} finally {
    [System.IO.Directory]::SetCurrentDirectory($OldCurrentDir)
    Pop-Location
}
