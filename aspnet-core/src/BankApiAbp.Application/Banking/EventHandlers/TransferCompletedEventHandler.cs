using System.Threading.Tasks;
using BankApiAbp.Banking.Events;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;

namespace BankApiAbp.Banking.EventHandlers;

public class TransferCompletedEventHandler :
    IDistributedEventHandler<TransferCompletedEto>,
    ITransientDependency
{
    private readonly ILogger<TransferCompletedEventHandler> _logger;

    public TransferCompletedEventHandler(
        ILogger<TransferCompletedEventHandler> logger)
    {
        _logger = logger;
    }

    public Task HandleEventAsync(TransferCompletedEto eventData)
    {
        _logger.LogInformation(
            " EVENT RECEIVED → TransferId={TransferId}, Amount={Amount}",
            eventData.TransferId,
            eventData.Amount);

        return Task.CompletedTask;
    }
}