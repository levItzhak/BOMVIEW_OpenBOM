using BOMVIEW.Models;
using System;

namespace BOMVIEW.Services
{
    public class PriceOptimizer
    {
        public class OptimizedPrice
        {
            public decimal OriginalTotalPrice { get; set; }
            public decimal OptimizedTotalPrice { get; set; }
            public int OriginalQuantity { get; set; }
            public int OptimizedQuantity { get; set; }
            public bool IsOptimized => OptimizedQuantity > OriginalQuantity;
            public decimal Savings => OriginalTotalPrice - OptimizedTotalPrice;
        }

        public static OptimizedPrice GetOptimizedPrice(SupplierData supplierData, int requiredQuantity)
        {
            if (supplierData?.PriceBreaks == null || !supplierData.PriceBreaks.Any())
            {
                return new OptimizedPrice
                {
                    OriginalTotalPrice = supplierData?.Price * requiredQuantity ?? 0,
                    OptimizedTotalPrice = supplierData?.Price * requiredQuantity ?? 0,
                    OriginalQuantity = requiredQuantity,
                    OptimizedQuantity = requiredQuantity
                };
            }

            var sortedBreaks = supplierData.PriceBreaks
                .OrderBy(pb => pb.Quantity)
                .ToList();

            // Get current price break
            var currentBreak = sortedBreaks
                .Where(pb => pb.Quantity <= requiredQuantity)
                .MaxBy(pb => pb.Quantity);

            decimal currentUnitPrice = currentBreak?.UnitPrice ?? supplierData.Price;
            decimal originalTotalPrice = currentUnitPrice * requiredQuantity;

            // Initialize with current values
            var optimized = new OptimizedPrice
            {
                OriginalTotalPrice = originalTotalPrice,
                OptimizedTotalPrice = originalTotalPrice,
                OriginalQuantity = requiredQuantity,
                OptimizedQuantity = requiredQuantity
            };

            // Check all higher quantity breaks for potential savings
            foreach (var priceBreak in sortedBreaks.Where(pb => pb.Quantity > requiredQuantity))
            {
                decimal totalAtBreak = priceBreak.UnitPrice * priceBreak.Quantity;

                if (totalAtBreak < optimized.OptimizedTotalPrice)
                {
                    optimized.OptimizedTotalPrice = totalAtBreak;
                    optimized.OptimizedQuantity = priceBreak.Quantity;
                }
            }

            return optimized;
        }
    }
}