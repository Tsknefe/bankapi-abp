using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BankApiAbp.Banking.Dtos;
using BankApiAbp.Banking.Messaging;
using Volo.Abp.Application.Services;

namespace BankApiAbp.Banking;

public interface IEventAdminAppService : IApplicationService
{
    Task<List<InboxMessageDto>> GetRetryableInboxMessagesAsync();
    Task RetryInboxMessageAsync(Guid inboxMessageId);
}