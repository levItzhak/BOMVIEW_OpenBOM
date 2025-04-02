namespace BOMVIEW.Models
{
    public class PriceBreak
    {
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }

    public class SupplierData
    {
        public string ProductUrl { get; set; }
        public string ImageUrl { get; set; }
        public string DatasheetUrl { get; set; }
        public decimal Price { get; set; }
        public int Availability { get; set; }
        public bool IsAvailable { get; set; }
        public string Description { get; set; }
        public string Manufacturer { get; set; }
        public string LeadTime { get; set; }
        public string Category { get; set; }
        public List<DigiKeyParameterInfo> Parameters { get; set; } = new List<DigiKeyParameterInfo>();
        public string DigiKeyPartNumber { get; set; }
        public string MouserPartNumber { get; set; }
        public string FarnellPartNumber { get; set; }
        public string IsraelPartNumber { get; set; }
        public string Supplier { get; set; }
        public string PartNumber { get; set; }


        public List<PriceBreak> PriceBreaks { get; set; } = new List<PriceBreak>();
        public (decimal currentPrice, decimal nextBreakPrice, int nextBreakQuantity)


            GetPriceForQuantity(int quantity)
        {
            if (PriceBreaks == null || !PriceBreaks.Any())
                return (Price, Price, 0);

            // Sort price breaks by quantity
            var sortedBreaks = PriceBreaks.OrderBy(pb => pb.Quantity).ToList();

            // Find current price break
            var currentBreak = sortedBreaks.Where(pb => pb.Quantity <= quantity)
                                         .MaxBy(pb => pb.Quantity);

            // Find next price break
            var nextBreak = sortedBreaks.Where(pb => pb.Quantity > quantity)
                                      .MinBy(pb => pb.Quantity);

            return (
                currentBreak?.UnitPrice ?? Price,
                nextBreak?.UnitPrice ?? currentBreak?.UnitPrice ?? Price,
                nextBreak?.Quantity ?? 0
            );



        }

        public (decimal currentPrice, decimal nextBreakPrice, int nextBreakQuantity, bool savesMoney)
    GetPriceWithSavingsCheck(int quantity)
        {
            if (PriceBreaks == null || !PriceBreaks.Any())
                return (Price, Price, 0, false);

            // Sort price breaks by quantity
            var sortedBreaks = PriceBreaks.OrderBy(pb => pb.Quantity).ToList();

            // Find current price break
            var currentBreak = sortedBreaks.Where(pb => pb.Quantity <= quantity)
                                         .MaxBy(pb => pb.Quantity);

            // Find next price break
            var nextBreak = sortedBreaks.Where(pb => pb.Quantity > quantity)
                                       .MinBy(pb => pb.Quantity);

            // Calculate current total cost
            decimal currentUnitPrice = currentBreak?.UnitPrice ?? Price;
            decimal currentTotalCost = currentUnitPrice * quantity;

            // Calculate next break total cost if available
            if (nextBreak != null)
            {
                decimal nextTotalCost = nextBreak.UnitPrice * nextBreak.Quantity;
                bool savesMoney = nextTotalCost < currentTotalCost;

                return (
                    currentBreak?.UnitPrice ?? Price,
                    nextBreak.UnitPrice,
                    nextBreak.Quantity,
                    savesMoney
                );
            }

            return (
                currentBreak?.UnitPrice ?? Price,
                currentBreak?.UnitPrice ?? Price,
                0,
                false
            );
        }


        public class SupplierParameter
        {
            public string Name { get; set; }
            public string Value { get; set; }
        }
    }
}