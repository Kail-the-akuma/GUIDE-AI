using System.Threading.Tasks;
namespace Lmp.Application;
public interface IHandler<TCommand, TResult>
{
    Task<TResult> Handle(TCommand command);
}