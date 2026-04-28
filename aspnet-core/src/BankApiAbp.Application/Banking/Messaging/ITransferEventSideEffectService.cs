using System.Threading.Tasks;

namespace BankApiAbp.Banking.Messaging;

public interface ITransferEventSideEffectService
{
    Task WriteAuditLogAsync(MoneyTransferredEto eventData);
    Task WriteNotificationLogAsync(MoneyTransferredEto eventData);
}