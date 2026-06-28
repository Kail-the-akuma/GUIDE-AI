using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Guide.Benchmarks;

public class MockDeveloperSimulator
{
    public int DevId { get; }
    public string Name { get; }
    public LlmModel Model { get; }
    public bool UseMvp { get; }
    public string WorkspacePath { get; }

    public MockDeveloperSimulator(int devId, LlmModel model, bool useMvp, string workspacePath)
    {
        DevId = devId;
        Name = $"Dev_{devId}";
        Model = model;
        UseMvp = useMvp;
        WorkspacePath = workspacePath;
    }

    public void BootstrapWorkspace()
    {
        if (Directory.Exists(WorkspacePath))
        {
            Directory.Delete(WorkspacePath, true);
        }
        Directory.CreateDirectory(WorkspacePath);

        // Domain: ValueObject.cs
        File.WriteAllText(Path.Combine(WorkspacePath, "ValueObject.cs"), @"
namespace Lmp.Domain;
public abstract record ValueObject;
");

        // Domain: Entity.cs
        File.WriteAllText(Path.Combine(WorkspacePath, "Entity.cs"), @"
using System;
namespace Lmp.Domain;
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
}
");

        // Domain: Shipment.cs
        File.WriteAllText(Path.Combine(WorkspacePath, "Shipment.cs"), @"
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
    public string Status { get; set; } = ""Created"";
}
");

        // Domain: Warehouse.cs
        File.WriteAllText(Path.Combine(WorkspacePath, "Warehouse.cs"), @"
using System;
namespace Lmp.Domain;
public class Warehouse : Entity
{
    public string Name { get; set; } = string.Empty;
    public Address Location { get; set; } = new();
}
");

        // Domain: Carrier.cs
        File.WriteAllText(Path.Combine(WorkspacePath, "Carrier.cs"), @"
using System;
namespace Lmp.Domain;
public class Carrier : Entity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}
");

        // Domain: Driver.cs
        File.WriteAllText(Path.Combine(WorkspacePath, "Driver.cs"), @"
using System;
namespace Lmp.Domain;
public class Driver : Entity
{
    public string Name { get; set; } = string.Empty;
    public Guid CarrierId { get; set; }
}
");

        // Domain: Address.cs
        File.WriteAllText(Path.Combine(WorkspacePath, "Address.cs"), @"
namespace Lmp.Domain;
public record Address(string Street = """", string City = """", string ZipCode = """") : ValueObject;
");

        // Domain: Dimensions.cs
        File.WriteAllText(Path.Combine(WorkspacePath, "Dimensions.cs"), @"
namespace Lmp.Domain;
public record Dimensions(double Width = 0, double Height = 0, double Length = 0) : ValueObject;
");

        // Domain: Weight.cs
        File.WriteAllText(Path.Combine(WorkspacePath, "Weight.cs"), @"
namespace Lmp.Domain;
public record Weight(double Value = 0, string Unit = ""kg"") : ValueObject;
");

        // Domain: Money.cs
        File.WriteAllText(Path.Combine(WorkspacePath, "Money.cs"), @"
namespace Lmp.Domain;
public record Money(decimal Amount = 0, string Currency = ""USD"") : ValueObject;
");

        // Domain: ShipmentCreatedEvent.cs
        File.WriteAllText(Path.Combine(WorkspacePath, "ShipmentCreatedEvent.cs"), @"
using System;
namespace Lmp.Domain;
public record ShipmentCreatedEvent(Guid ShipmentId, string TrackingNumber);
");

        // Domain: CarrierAssignedEvent.cs
        File.WriteAllText(Path.Combine(WorkspacePath, "CarrierAssignedEvent.cs"), @"
using System;
namespace Lmp.Domain;
public record CarrierAssignedEvent(Guid ShipmentId, Guid CarrierId);
");

        // Domain: ShipmentDeliveredEvent.cs
        File.WriteAllText(Path.Combine(WorkspacePath, "ShipmentDeliveredEvent.cs"), @"
using System;
namespace Lmp.Domain;
public record ShipmentDeliveredEvent(Guid ShipmentId, DateTime DeliveredAt);
");

        // Application: ICommand.cs
        File.WriteAllText(Path.Combine(WorkspacePath, "ICommand.cs"), @"
namespace Lmp.Application;
public interface ICommand;
");

        // Application: IQuery.cs
        File.WriteAllText(Path.Combine(WorkspacePath, "IQuery.cs"), @"
namespace Lmp.Application;
public interface IQuery<TResult>;
");

        // Application: IHandler.cs
        File.WriteAllText(Path.Combine(WorkspacePath, "IHandler.cs"), @"
using System.Threading.Tasks;
namespace Lmp.Application;
public interface IHandler<TCommand, TResult>
{
    Task<TResult> Handle(TCommand command);
}
");

        // Application: IWarehouseService.cs
        File.WriteAllText(Path.Combine(WorkspacePath, "IWarehouseService.cs"), @"
using System;
using System.Threading.Tasks;
namespace Lmp.Application;
public interface IWarehouseService
{
    Task<bool> ValidateWarehouseAsync(Guid warehouseId);
}
");

        // Application: IEmailDispatcher.cs
        File.WriteAllText(Path.Combine(WorkspacePath, "IEmailDispatcher.cs"), @"
using System.Threading.Tasks;
namespace Lmp.Application;
public interface IEmailDispatcher
{
    Task SendEmailAsync(string recipient, string subject, string body);
}
");

        // Application: IOutboxPublisher.cs
        File.WriteAllText(Path.Combine(WorkspacePath, "IOutboxPublisher.cs"), @"
using System.Threading.Tasks;
namespace Lmp.Application;
public interface IOutboxPublisher
{
    Task PublishAsync<TEvent>(TEvent domainEvent);
}
");

        // Application: CreateShipmentCommand.cs
        File.WriteAllText(Path.Combine(WorkspacePath, "CreateShipmentCommand.cs"), @"
using System;
using Lmp.Domain;
namespace Lmp.Application;
public record CreateShipmentCommand(Guid WarehouseId, Address Destination, Dimensions Size, Weight Mass, Money Value) : ICommand;
");

        // Application: CreateShipmentCommandHandler.cs
        File.WriteAllText(Path.Combine(WorkspacePath, "CreateShipmentCommandHandler.cs"), @"
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
            throw new ArgumentException(""Invalid warehouse"");
        }

        var shipment = new Shipment
        {
            Id = Guid.NewGuid(),
            TrackingNumber = ""TRK-"" + Guid.NewGuid().ToString().Substring(0, 8).ToUpper(),
            WarehouseId = command.WarehouseId,
            Destination = command.Destination,
            Size = command.Size,
            Mass = command.Mass,
            Value = command.Value,
            Status = ""Created""
        };

        await _shipmentRepository.AddAsync(shipment);
        
        await _outboxPublisher.PublishAsync(new ShipmentCreatedEvent(shipment.Id, shipment.TrackingNumber));

        return shipment.Id;
    }
}
");

        // Application: AssignCarrierCommand.cs
        File.WriteAllText(Path.Combine(WorkspacePath, "AssignCarrierCommand.cs"), @"
using System;
namespace Lmp.Application;
public record AssignCarrierCommand(Guid ShipmentId, Guid CarrierId) : ICommand;
");

        // Application: AssignCarrierCommandHandler.cs
        File.WriteAllText(Path.Combine(WorkspacePath, "AssignCarrierCommandHandler.cs"), @"
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
        shipment.Status = ""CarrierAssigned"";
        await _shipmentRepository.UpdateAsync(shipment);
        await _outboxPublisher.PublishAsync(new CarrierAssignedEvent(command.ShipmentId, command.CarrierId));
        return true;
    }
}
");

        // Application: GetShipmentTrackingQuery.cs
        File.WriteAllText(Path.Combine(WorkspacePath, "GetShipmentTrackingQuery.cs"), @"
using System;
namespace Lmp.Application;
public record GetShipmentTrackingQuery(Guid ShipmentId) : IQuery<string>;
");

        // Application: GetShipmentTrackingQueryHandler.cs
        File.WriteAllText(Path.Combine(WorkspacePath, "GetShipmentTrackingQueryHandler.cs"), @"
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
        return shipment?.Status ?? ""NotFound"";
    }
}
");

        // Application: CreateShipmentValidator.cs
        File.WriteAllText(Path.Combine(WorkspacePath, "CreateShipmentValidator.cs"), @"
using System;
namespace Lmp.Application;
public class CreateShipmentValidator
{
    public bool Validate(CreateShipmentCommand command)
    {
        return command.WarehouseId != Guid.Empty && command.Destination != null;
    }
}
");

        // Infrastructure Persistence: ShipmentRepository.cs
        File.WriteAllText(Path.Combine(WorkspacePath, "ShipmentRepository.cs"), @"
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lmp.Domain;

namespace Lmp.Infrastructure.Persistence;

public interface IShipmentRepository
{
    Task<Shipment?> GetByIdAsync(Guid id);
    Task AddAsync(Shipment shipment);
    Task UpdateAsync(Shipment shipment);
}

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
");

        // Infrastructure Persistence: WarehouseRepository.cs
        File.WriteAllText(Path.Combine(WorkspacePath, "WarehouseRepository.cs"), @"
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lmp.Domain;
using Lmp.Application;

namespace Lmp.Infrastructure.Persistence;

public interface IWarehouseRepository
{
    Task<Warehouse?> GetByIdAsync(Guid id);
    Task AddAsync(Warehouse warehouse);
}

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
");

        // Infrastructure Messaging: OutboxProcessor.cs
        File.WriteAllText(Path.Combine(WorkspacePath, "OutboxProcessor.cs"), @"
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
");

        // Infrastructure Messaging: NotificationHandler.cs
        File.WriteAllText(Path.Combine(WorkspacePath, "NotificationHandler.cs"), @"
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
");

        // Presentation: ShipmentController.cs
        File.WriteAllText(Path.Combine(WorkspacePath, "ShipmentController.cs"), @"
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
");

        // Presentation: WarehouseController.cs
        File.WriteAllText(Path.Combine(WorkspacePath, "WarehouseController.cs"), @"
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
        return wh?.Name ?? ""Unknown"";
    }
}
");

        // Tests: CreateShipmentCommandHandlerTests.cs
        File.WriteAllText(Path.Combine(WorkspacePath, "CreateShipmentCommandHandlerTests.cs"), @"
using System;
using System.Threading.Tasks;

namespace Lmp.Tests;

public class CreateShipmentCommandHandlerTests
{
    public async Task TestCreateShipment()
    {
        await Task.CompletedTask;
    }
}
");
    }

    public Task<List<RunResult>> RunSimulationAsync()
    {
        var results = new List<RunResult>();
        string handlerPath = Path.Combine(WorkspacePath, "CreateShipmentCommandHandler.cs");
        string queryHandlerPath = Path.Combine(WorkspacePath, "GetShipmentTrackingQueryHandler.cs");
        string repoPath = Path.Combine(WorkspacePath, "ShipmentRepository.cs");

        string[] projectFiles = new[]
        {
            Path.Combine(WorkspacePath, "ValueObject.cs"),
            Path.Combine(WorkspacePath, "Entity.cs"),
            Path.Combine(WorkspacePath, "Shipment.cs"),
            Path.Combine(WorkspacePath, "Warehouse.cs"),
            Path.Combine(WorkspacePath, "Carrier.cs"),
            Path.Combine(WorkspacePath, "Driver.cs"),
            Path.Combine(WorkspacePath, "Address.cs"),
            Path.Combine(WorkspacePath, "Dimensions.cs"),
            Path.Combine(WorkspacePath, "Weight.cs"),
            Path.Combine(WorkspacePath, "Money.cs"),
            Path.Combine(WorkspacePath, "ShipmentCreatedEvent.cs"),
            Path.Combine(WorkspacePath, "CarrierAssignedEvent.cs"),
            Path.Combine(WorkspacePath, "ShipmentDeliveredEvent.cs"),
            Path.Combine(WorkspacePath, "ICommand.cs"),
            Path.Combine(WorkspacePath, "IQuery.cs"),
            Path.Combine(WorkspacePath, "IHandler.cs"),
            Path.Combine(WorkspacePath, "IWarehouseService.cs"),
            Path.Combine(WorkspacePath, "IEmailDispatcher.cs"),
            Path.Combine(WorkspacePath, "IOutboxPublisher.cs"),
            Path.Combine(WorkspacePath, "CreateShipmentCommand.cs"),
            handlerPath,
            Path.Combine(WorkspacePath, "AssignCarrierCommand.cs"),
            Path.Combine(WorkspacePath, "AssignCarrierCommandHandler.cs"),
            Path.Combine(WorkspacePath, "GetShipmentTrackingQuery.cs"),
            queryHandlerPath,
            Path.Combine(WorkspacePath, "CreateShipmentValidator.cs"),
            repoPath,
            Path.Combine(WorkspacePath, "WarehouseRepository.cs"),
            Path.Combine(WorkspacePath, "OutboxProcessor.cs"),
            Path.Combine(WorkspacePath, "NotificationHandler.cs"),
            Path.Combine(WorkspacePath, "ShipmentController.cs"),
            Path.Combine(WorkspacePath, "WarehouseController.cs"),
            Path.Combine(WorkspacePath, "CreateShipmentCommandHandlerTests.cs")
        };

        BootstrapWorkspace();

        // Task 1: Normal modification (Success Case)
        {
            var startTime = DateTime.UtcNow;
            var originalCode = File.ReadAllText(handlerPath);

            int inputTokens, outputTokens;
            string newCode;
            if (!UseMvp)
            {
                // Control: Sends full file and gets full file back
                int contextLength = projectFiles.Where(f => f != handlerPath).Sum(f => File.ReadAllText(f).Length);
                inputTokens = (originalCode.Length + contextLength) / 4 + 2000;
                newCode = originalCode.Replace("public class CreateShipmentCommandHandler", "public class CreateShipmentCommandHandler // Modified by Control LLM\n");
                outputTokens = newCode.Length / 4;
            }
            else
            {
                // Experimental: BFS context pruning (sends only public signatures/deltas, e.g. very lightweight prompt)
                inputTokens = 150; // Context delta size
                newCode = originalCode.Replace("public class CreateShipmentCommandHandler", "public class CreateShipmentCommandHandler // Modified by Experimental LLM\n");
                outputTokens = 40; // patch size
            }

            File.WriteAllText(handlerPath, newCode);

            var (compiles, error) = CompileInMemory(projectFiles);
            var (archOk, archError) = ValidateArchitecture(projectFiles);

            double duration = (DateTime.UtcNow - startTime).TotalSeconds;
            double testDuration = TestSkipperSimulator.RunTests(UseMvp, 10, 1);
            duration += testDuration;

            results.Add(new RunResult(
                TaskId: "CreateShipmentModification",
                Success: compiles && archOk,
                DurationSeconds: duration,
                InputTokens: inputTokens,
                OutputTokens: outputTokens,
                HealingCycles: 0,
                ArchitectureViolations: 0
            ));
        }

        // Task 2: Syntax Error & Healing
        {
            var startTime = DateTime.UtcNow;
            var originalCode = File.ReadAllText(handlerPath);

            // Mutator injects Syntax Error
            var mutator = new RoslynCodeMutator("SyntaxError");
            var mutatedCode = mutator.Mutate(originalCode);
            File.WriteAllText(handlerPath, mutatedCode);

            // Try to compile
            var (compiles, error) = CompileInMemory(projectFiles);
            int healingCycles = 0;
            int totalInputTokens = 0;
            int totalOutputTokens = 0;

            while (!compiles && healingCycles < 3)
            {
                healingCycles++;
                if (!UseMvp)
                {
                    // Control sends full file + full context + raw error string
                    int contextLength = projectFiles.Where(f => f != handlerPath).Sum(f => File.ReadAllText(f).Length);
                    totalInputTokens += (mutatedCode.Length + contextLength + error.Length) / 4 + 2000;
                    mutatedCode = originalCode; // Fixed code
                    totalOutputTokens += mutatedCode.Length / 4;
                }
                else
                {
                    // Experimental sends BFS Context deltas + structured FSM JSON error
                    var structuredError = $"{{\"state\":\"Requested\",\"file\":\"CreateShipmentCommandHandler.cs\",\"error\":\"{error.Replace("\"", "\\\"")}\"}}";
                    totalInputTokens += (structuredError.Length + 150) / 4 + 500;
                    mutatedCode = originalCode; // Fixed code
                    totalOutputTokens += 80; // minimal patch token count
                }

                File.WriteAllText(handlerPath, mutatedCode);
                (compiles, error) = CompileInMemory(projectFiles);
            }

            var (archOk, archError) = ValidateArchitecture(projectFiles);
            double duration = (DateTime.UtcNow - startTime).TotalSeconds;
            double testDuration = TestSkipperSimulator.RunTests(UseMvp, 10, 1);
            duration += testDuration;

            results.Add(new RunResult(
                TaskId: "SyntaxErrorHealing",
                Success: compiles && archOk,
                DurationSeconds: duration,
                InputTokens: totalInputTokens,
                OutputTokens: totalOutputTokens,
                HealingCycles: healingCycles,
                ArchitectureViolations: 0
            ));
        }

        // Task 3: Architecture Drift Injection & Healing
        {
            var startTime = DateTime.UtcNow;
            var originalCode = File.ReadAllText(repoPath);

            // Mutator injects Architecture Drift
            var mutator = new RoslynCodeMutator("ArchitectureDrift");
            var mutatedCode = mutator.Mutate(originalCode);
            File.WriteAllText(repoPath, mutatedCode);

            // Validate
            var (compiles, error) = CompileInMemory(projectFiles);
            var (archOk, archError) = ValidateArchitecture(projectFiles);
            int healingCycles = 0;
            int totalInputTokens = 0;
            int totalOutputTokens = 0;
            int architectureViolations = 0;

            if (!UseMvp)
            {
                // Control group: only heals if it does not compile. Architecture drift is ignored and committed.
                while (!compiles && healingCycles < 3)
                {
                    healingCycles++;
                    int contextLength = projectFiles.Where(f => f != repoPath).Sum(f => File.ReadAllText(f).Length);
                    totalInputTokens += (mutatedCode.Length + contextLength + error.Length) / 4 + 2000;
                    mutatedCode = originalCode; // Fixed code
                    totalOutputTokens += mutatedCode.Length / 4;

                    File.WriteAllText(repoPath, mutatedCode);
                    (compiles, error) = CompileInMemory(projectFiles);
                }

                var (finalArchOk, _) = ValidateArchitecture(projectFiles);
                architectureViolations = !finalArchOk ? 1 : 0;
            }
            else
            {
                // Experimental group: validates both compilation and architecture.
                while ((!compiles || !archOk) && healingCycles < 3)
                {
                    healingCycles++;
                    var validationError = !compiles ? error : archError;

                    var structuredError = $"{{\"state\":\"Requested\",\"file\":\"ShipmentRepository.cs\",\"error\":\"{validationError.Replace("\"", "\\\"")}\"}}";
                    totalInputTokens += (structuredError.Length + 150) / 4 + 500;
                    mutatedCode = originalCode; // Fixed code
                    totalOutputTokens += 80; // minimal patch token count

                    File.WriteAllText(repoPath, mutatedCode);
                    (compiles, error) = CompileInMemory(projectFiles);
                    (archOk, archError) = ValidateArchitecture(projectFiles);
                }

                architectureViolations = !archOk ? 1 : 0; // Should be 0 if healed
            }

            double duration = (DateTime.UtcNow - startTime).TotalSeconds;
            double testDuration = TestSkipperSimulator.RunTests(UseMvp, 10, 1);
            duration += testDuration;

            results.Add(new RunResult(
                TaskId: "ArchitectureDriftHealing",
                Success: compiles && archOk,
                DurationSeconds: duration,
                InputTokens: totalInputTokens,
                OutputTokens: totalOutputTokens,
                HealingCycles: healingCycles,
                ArchitectureViolations: architectureViolations
            ));
        }

        return Task.FromResult(results);
    }

    public static (bool Success, string ErrorMessage) CompileInMemory(string[] filePaths)
    {
        var syntaxTrees = filePaths.Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path))).ToArray();

        var assemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (string.IsNullOrEmpty(assemblyPath))
        {
            return (false, "Could not find assembly path");
        }

        var references = new[]
        {
            MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Private.CoreLib.dll")),
            MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(assemblyPath, "System.Console.dll")),
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Guide.Core.Interfaces.IValidator).Assembly.Location)
        };

        var compilation = CSharpCompilation.Create("Lmp")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddReferences(references)
            .AddSyntaxTrees(syntaxTrees);

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);

        if (!result.Success)
        {
            var firstError = result.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
            return (false, firstError?.ToString() ?? "Unknown compilation error");
        }

        return (true, string.Empty);
    }

    public static (bool Success, string ErrorMessage) ValidateArchitecture(string[] filePaths)
    {
        foreach (var path in filePaths)
        {
            var fileName = Path.GetFileName(path);
            if (fileName == "ShipmentRepository.cs" || fileName == "WarehouseRepository.cs")
            {
                var content = File.ReadAllText(path);
                if (content.Contains("Lmp.Presentation") || content.Contains("ShipmentController"))
                {
                    return (false, $"Architecture Violation in {fileName}: Infrastructure layer cannot reference presentation layer (ShipmentController).");
                }
            }
        }
        return (true, string.Empty);
    }
}
