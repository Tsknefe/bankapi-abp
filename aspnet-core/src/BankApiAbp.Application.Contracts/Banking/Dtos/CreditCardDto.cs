using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BankApiAbp.Banking.Dtos
{
    public class CreditCardDto
    {
        public Guid Id { get; set; }
        public Guid CustomerId { get; set; }
        public string CardNo { get; set; } = default!;
        public DateTime ExpireAt { get; set; }
        public decimal Limit { get; set; }
        public decimal CurrentDebt { get; set; }
        public bool IsActive { get; set; }
    }

}
