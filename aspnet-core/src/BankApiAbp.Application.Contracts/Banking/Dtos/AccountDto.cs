using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BankApiAbp.Banking.Dtos
{
    public class AccountDto
    {
        public Guid Id { get; set; }
        public Guid CustomerId { get; set; }
        public string Name { get; set; } = default!;
        public string Iban { get; set; }= default!;

        public decimal Balance { get; set; }
        public AccountType AccountType { get; set; }
        public bool IsActive { get; set; }

    }
}
