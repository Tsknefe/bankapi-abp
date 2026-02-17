using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BankApiAbp.Banking.Dtos
{
    public class TransactionDto
    {
        public Guid Id { get; set; }
        public TransactionType TxType { get; set; }
        public decimal Amount { get; set; }
        public string? Description { get; set; }
        public DateTime CreationTime { get; set; }
        public Guid? AccountId { get; set; }
        public Guid? DebitCardId { get; set; }
        public Guid? CreditCardId { get; set; }
    }
}
