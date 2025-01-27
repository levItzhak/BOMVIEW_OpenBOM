using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace BOMVIEW.Models
{
    public class MouserProductResponse
    {
        [JsonPropertyName("SearchResults")]
        public SearchResults SearchResults { get; set; }
    }

    public class SearchResults
    {
        [JsonPropertyName("Parts")]
        public List<MouserProductDetails> Parts { get; set; }
    }

    public class MouserProductDetails
    {
        [JsonPropertyName("PriceBreaks")]
        public List<MouserPriceBreak> PriceBreaks { get; set; }

        [JsonPropertyName("AvailabilityInStock")]
        public string AvailabilityInStock { get; set; }

        [JsonPropertyName("MouserPartNumber")]
        public string MouserPartNumber { get; set; }
    }

    public class MouserPriceBreak
    {
        [JsonPropertyName("Price")]
        public string Price { get; set; }

        [JsonPropertyName("Quantity")]
        public int Quantity { get; set; }
    }
}