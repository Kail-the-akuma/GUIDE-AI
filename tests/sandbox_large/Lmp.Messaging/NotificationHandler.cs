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