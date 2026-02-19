using System;
using System.Linq;
using System.Threading.Tasks;
using BankApiAbp.Banking.Dtos;
using BankApiAbp.Entities;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Repositories;

namespace BankApiAbp.Banking;

public partial class BankingAppService
{
    public async Task<IdResponseDto> CreateCustomerAsync(CreateCustomerDto input)
    {
        var userId = CurrentUserIdOrThrow();

        var existing = await _customers.FirstOrDefaultAsync(x => x.TcNo == input.TcNo && x.UserId == userId);
        if (existing != null)
            throw new UserFriendlyException("This customer already exists with TcNo");

        var customer = new Customer(
            GuidGenerator.Create(),
            userId,
            input.Name,
            input.TcNo,
            input.BirthDate,
            input.BirthPlace
        );

        await _customers.InsertAsync(customer, autoSave: true);
        return new IdResponseDto { Id = customer.Id };
    }

    public async Task<PagedResultDto<CustomerListItemDto>> GetMyCustomersAsync(CustomerListInput input)
    {
        var userId = CurrentUserIdOrThrow();

        var q = (await _customers.GetQueryableAsync())
            .Where(x => x.UserId == userId);

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var f = input.Filter.Trim();
            q = q.Where(x =>
                x.Name.Contains(f) ||
                x.TcNo.Contains(f) ||
                x.BirthPlace.Contains(f));
        }

        var total = await AsyncExecuter.CountAsync(q);

        q = q.OrderBy(x => x.Name);

        var items = await AsyncExecuter.ToListAsync(
            q.Skip(input.SkipCount).Take(input.MaxResultCount)
        );

        return new PagedResultDto<CustomerListItemDto>(
            total,
            items.Select(c => new CustomerListItemDto
            {
                Id = c.Id,
                Name = c.Name,
                TcNo = c.TcNo,
                BirthPlace = c.BirthPlace
            }).ToList()
        );
    }

    private async Task<Customer> GetCustomerOwnedAsync(Guid customerId)
    {
        var userId = CurrentUserIdOrThrow();

        var cust = await _customers.FindAsync(customerId);
        if (cust == null) throw new UserFriendlyException("Müşteri bulunamadı.");

        if (cust.UserId != userId)
            throw new AbpAuthorizationException("Bu müşteriye erişimin yok.");

        return cust;
    }
}
