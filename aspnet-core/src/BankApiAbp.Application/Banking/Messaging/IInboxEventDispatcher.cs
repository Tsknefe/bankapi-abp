using System;
using System.Threading.Tasks;

namespace BankApiAbp.Banking.Messaging;

public interface IInboxEventDispatcher
{
    Task DispatchAsync(Guid inboxMessageId);
}