using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;

namespace BankApiAbp.Banking.Dtos
{
    public class GetTransactionsInput: PagedAndSortedResultRequestDto
    {
        public Guid? AccountId { get; set; }
        public Guid? DebitCardId { get; set; }
        public Guid? CreditCardId { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }

        public GetTransactionsInput()
        {
            Sorting = "CreationTime DESC";
            MaxResultCount = 50;
            SkipCount = 0;
        }
    }
}
