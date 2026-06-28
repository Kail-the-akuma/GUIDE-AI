using System.Threading.Tasks;
namespace Lmp.Application;
public interface IEmailDispatcher
{
    Task SendEmailAsync(string recipient, string subject, string body);
}