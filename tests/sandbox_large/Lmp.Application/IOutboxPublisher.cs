using System.Threading.Tasks;
namespace Lmp.Application;
public interface IOutboxPublisher
{
    Task PublishAsync<TEvent>(TEvent domainEvent);
}