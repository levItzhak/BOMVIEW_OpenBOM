using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace BOMVIEW.Models
{
    public class DigiKeyProductResponse
    {
        [JsonPropertyName("Product")]
        public DigiKeyProduct Product { get; set; }
    }

    public class DigiKeyProduct
    {
        [JsonPropertyName("Description")]
        public DigiKeyDescription Description { get; set; }

        [JsonPropertyName("Manufacturer")]
        public DigiKeyManufacturer Manufacturer { get; set; }

        [JsonPropertyName("UnitPrice")]
        public decimal UnitPrice { get; set; }

        [JsonPropertyName("ProductUrl")]
        public string ProductUrl { get; set; }

        [JsonPropertyName("DatasheetUrl")]
        public string DatasheetUrl { get; set; }

        [JsonPropertyName("PhotoUrl")]
        public string PhotoUrl { get; set; }

        [JsonPropertyName("ProductVariations")]
        public List<DigiKeyProductVariation> ProductVariations { get; set; }

        [JsonPropertyName("Parameters")]
        public DigiKeyParameter[] Parameters { get; set; }

        [JsonPropertyName("QuantityAvailable")]
        public decimal QuantityAvailable { get; set; }

        [JsonPropertyName("ManufacturerLeadWeeks")]
        public string ManufacturerLeadWeeks { get; set; }

        [JsonPropertyName("IsAvailable")]
        public bool IsAvailable { get; set; } = true;

        [JsonPropertyName("Category")]
        public DigiKeyCategory Category { get; set; }

        [JsonPropertyName("DigiKeyProductNumber")]
        public string DigiKeyProductNumber { get; set; }

    }

    public class DigiKeyDescription
    {
        [JsonPropertyName("ProductDescription")]
        public string ProductDescription { get; set; }
    }

    public class DigiKeyManufacturer
    {
        [JsonPropertyName("Name")]
        public string Name { get; set; }
    }

    public class DigiKeyParameter
    {
        [JsonPropertyName("ParameterText")]
        public string ParameterText { get; set; }

        [JsonPropertyName("ValueText")]
        public string ValueText { get; set; }
    }


    public class DigiKeyCategory
    {
        [JsonPropertyName("Name")]
        public string Name { get; set; }
    }


    public class DigiKeyUrl
    {
        [JsonPropertyName("Supplier Link")]
        public string SupplierLink { get; set; }
    }

    public class DigiKeyProductVariation
    {

        [JsonPropertyName("DigiKeyProductNumber")]
        public string DigiKeyProductNumber { get; set; }

        [JsonPropertyName("StandardPricing")]
        public List<DigiKeyStandardPricing> StandardPricing { get; set; }

        [JsonPropertyName("QuantityAvailableforPackageType")]
        public int QuantityAvailableforPackageType { get; set; }

        [JsonPropertyName("MinimumOrderQuantity")]
        public int MinimumOrderQuantity { get; set; }
    }

    public class DigiKeyStandardPricing
    {
        [JsonPropertyName("BreakQuantity")]
        public int BreakQuantity { get; set; }

        [JsonPropertyName("UnitPrice")]
        public decimal UnitPrice { get; set; }

        [JsonPropertyName("TotalPrice")]
        public decimal TotalPrice { get; set; }
    }

    public class DigiKeyTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; }
    }

    public class DigiKeyParameterInfo
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }
}