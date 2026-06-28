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