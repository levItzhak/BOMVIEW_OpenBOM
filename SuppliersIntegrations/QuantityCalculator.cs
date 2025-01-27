using System;
using System.Linq;

namespace BOMVIEW.Services
{
    public static class QuantityCalculator
    {
        public static int CalculateMinimumOrderQuantity(decimal unitPrice, int originalQuantity)
        {
            if (unitPrice < 0.1m)
            {
                return Math.Max(originalQuantity, 100);
            }
            else if (unitPrice < 1.0m)
            {
                return Math.Max(originalQuantity, 10);
            }
            return originalQuantity;
        }

        public static (int optimizedQuantity, decimal totalPrice) GetOptimizedQuantity(
            decimal currentUnitPrice,
            int requiredQuantity,
            decimal? nextBreakPrice = null,
            int? nextBreakQty = null)
        {
            // First apply minimum order quantity rules
            int minOrderQty = CalculateMinimumOrderQuantity(currentUnitPrice, requiredQuantity);
            decimal baseTotal = currentUnitPrice * minOrderQty;

            // If no next price break, return the minimum order quantity result
            if (!nextBreakPrice.HasValue || !nextBreakQty.HasValue)
            {
                return (minOrderQty, baseTotal);
            }

            // Calculate total at next break point
            decimal nextBreakTotal = nextBreakPrice.Value * nextBreakQty.Value;

            // Return the quantity that gives the lowest total price
            if (nextBreakTotal < baseTotal)
            {
                return (nextBreakQty.Value, nextBreakTotal);
            }

            return (minOrderQty, baseTotal);
        }

        public static (int optimizedQuantity, decimal totalPrice, string supplier)
        GetBestSupplierQuantity(
            (int qty, decimal price) digiKeyOpt,
            (int qty, decimal price) mouserOpt)
        {
            if (digiKeyOpt.price <= mouserOpt.price)
            {
                return (digiKeyOpt.qty, digiKeyOpt.price, "DigiKey");
            }
            return (mouserOpt.qty, mouserOpt.price, "Mouser");
        }
    }
}