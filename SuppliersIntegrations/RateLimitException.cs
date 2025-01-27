using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


using BOMVIEW.Interfaces;
namespace BOMVIEW.Exceptions
{
    public class RateLimitException : Exception
    {
        public SupplierType Supplier { get; }

        public RateLimitException(SupplierType supplier, string message)
            : base(message)
        {
            Supplier = supplier;
        }
    }
}