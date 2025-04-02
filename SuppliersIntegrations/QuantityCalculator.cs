using System;
using System.Collections.Generic;
using System.Linq;
using BOMVIEW.Models;

namespace BOMVIEW
{
    public static class QuantityCalculator
    {
        public static decimal GetBestPriceForQuantity(SupplierData supplierData, int quantity)
        {
            if (supplierData == null || supplierData.PriceBreaks == null || !supplierData.PriceBreaks.Any())
                return supplierData?.Price ?? 0;

            // Sort price breaks by quantity
            var sortedBreaks = supplierData.PriceBreaks.OrderBy(pb => pb.Quantity).ToList();

            // Find the highest price break that is <= the requested quantity
            var applicable = sortedBreaks.Where(pb => pb.Quantity <= quantity)
                                      .MaxBy(pb => pb.Quantity);

            // If no applicable price break found (quantity is less than smallest break), use the first break
            if (applicable == null && sortedBreaks.Any())
            {
                // Use the first price break when the quantity is less than the minimum quantity
                applicable = sortedBreaks.First();
            }

            // Return the price from the applicable price break, or the default price if no applicable break
            return applicable?.UnitPrice ?? supplierData.Price;
        }

        public static decimal CalculateTotalPrice(SupplierData supplierData, int quantity)
        {
            decimal unitPrice = GetBestPriceForQuantity(supplierData, quantity);
            return unitPrice * quantity;
        }

        public static (decimal unitPrice, decimal totalPrice) FindOptimalPriceBreak(SupplierData supplierData, int desiredQuantity)
        {
            if (supplierData?.PriceBreaks == null || !supplierData.PriceBreaks.Any())
                return (supplierData?.Price ?? 0, (supplierData?.Price ?? 0) * desiredQuantity);

            // Sort price breaks by quantity
            var sortedBreaks = supplierData.PriceBreaks.OrderBy(pb => pb.Quantity).ToList();

            // Calculate total costs at each price break
            var costsAtBreakPoints = sortedBreaks
                .Select(pb => (
                    BreakQuantity: pb.Quantity,
                    UnitPrice: pb.UnitPrice,
                    TotalPrice: pb.UnitPrice * Math.Max(pb.Quantity, desiredQuantity)
                ))
                .ToList();

            // Also consider the exact desired quantity with its applicable price
            var priceForDesiredQty = GetBestPriceForQuantity(supplierData, desiredQuantity);
            costsAtBreakPoints.Add((
                BreakQuantity: desiredQuantity,
                UnitPrice: priceForDesiredQty,
                TotalPrice: priceForDesiredQty * desiredQuantity
            ));

            // Find the option with the lowest total cost
            var optimalOption = costsAtBreakPoints
                .Where(c => c.BreakQuantity >= desiredQuantity) // Only consider options that meet or exceed desired qty
                .OrderBy(c => c.TotalPrice)
                .FirstOrDefault();

            // If no option meets the desired quantity, fall back to the direct calculation
            if (optimalOption == default)
            {
                return (priceForDesiredQty, priceForDesiredQty * desiredQuantity);
            }

            return (optimalOption.UnitPrice, optimalOption.TotalPrice);
        }

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