using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BOMVIEW.Models
{
    // Response when searching by product ID
    public class FarnellProductResponse
    {
        [JsonPropertyName("premierFarnellPartNumberReturn")]
        public PremierFarnellPartNumberReturn PremierFarnellPartNumberReturn { get; set; }

        [JsonPropertyName("manufacturerPartNumberSearchReturn")]
        public ManufacturerPartNumberSearchReturn ManufacturerPartNumberSearchReturn { get; set; }

        [JsonPropertyName("keywordSearchReturn")]
        public KeywordSearchReturn KeywordSearchReturn { get; set; }
    }

    public class PremierFarnellPartNumberReturn
    {
        [JsonPropertyName("numberOfResults")]
        public int NumberOfResults { get; set; }

        [JsonPropertyName("products")]
        public List<FarnellProduct> Products { get; set; }
    }

    public class ManufacturerPartNumberSearchReturn
    {
        [JsonPropertyName("numberOfResults")]
        public int NumberOfResults { get; set; }

        [JsonPropertyName("products")]
        public List<FarnellProduct> Products { get; set; }
    }

    public class KeywordSearchReturn
    {
        [JsonPropertyName("numberOfResults")]
        public int NumberOfResults { get; set; }

        [JsonPropertyName("products")]
        public List<FarnellProduct> Products { get; set; }
    }

    // Common product model
    public class FarnellProduct
    {
        [JsonPropertyName("sku")]
        public string Sku { get; set; }

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; }

        [JsonPropertyName("packSize")]
        public int PackSize { get; set; }

        [JsonPropertyName("unitOfMeasure")]
        public string UnitOfMeasure { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("brandName")]
        public string BrandName { get; set; }

        [JsonPropertyName("translatedManufacturerPartNumber")]
        public string ManufacturerPartNumber { get; set; }

        [JsonPropertyName("translatedMinimumOrderQuality")]
        public int MinimumOrderQuantity { get; set; }

        [JsonPropertyName("publishingModule")]
        public string PublishingModule { get; set; }

        [JsonPropertyName("prices")]
        public List<FarnellPrice> Prices { get; set; }

        [JsonPropertyName("datasheets")]
        public List<FarnellDatasheet> Datasheets { get; set; }

        [JsonPropertyName("stock")]
        public FarnellStock Stock { get; set; }

        [JsonPropertyName("productStatus")]
        public string ProductStatus { get; set; }

        [JsonPropertyName("image")]
        public FarnellImage Image { get; set; }
    }

    public class FarnellPrice
    {
        [JsonPropertyName("from")]
        public int From { get; set; }

        [JsonPropertyName("to")]
        public int To { get; set; }

        [JsonPropertyName("cost")]
        public decimal Cost { get; set; }
    }

    public class FarnellStock
    {
        [JsonPropertyName("level")]
        public int Level { get; set; }

        [JsonPropertyName("leastLeadTime")]
        public int LeastLeadTime { get; set; }

        [JsonPropertyName("status")]
        public int Status { get; set; }

        [JsonPropertyName("shipsFromMultipleWarehouses")]
        public bool ShipsFromMultipleWarehouses { get; set; }

        [JsonPropertyName("breakdown")]
        public List<FarnellStockBreakdown> Breakdown { get; set; }

        [JsonPropertyName("regionalBreakdown")]
        public List<FarnellRegionalBreakdown> RegionalBreakdown { get; set; }
    }

    public class FarnellStockBreakdown
    {
        [JsonPropertyName("inv")]
        public int Inv { get; set; }

        [JsonPropertyName("region")]
        public string Region { get; set; }

        [JsonPropertyName("lead")]
        public int Lead { get; set; }

        [JsonPropertyName("warehouse")]
        public string Warehouse { get; set; }
    }

    public class FarnellRegionalBreakdown
    {
        [JsonPropertyName("level")]
        public int Level { get; set; }

        [JsonPropertyName("leastLeadTime")]
        public int LeastLeadTime { get; set; }

        [JsonPropertyName("status")]
        public int Status { get; set; }

        [JsonPropertyName("warehouse")]
        public string Warehouse { get; set; }

        [JsonPropertyName("shipsFromMultipleWarehouses")]
        public bool ShipsFromMultipleWarehouses { get; set; }
    }

    public class FarnellDatasheet
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }
    }

    public class FarnellImage
    {
        [JsonPropertyName("baseName")]
        public string BaseName { get; set; }

        [JsonPropertyName("vrntPath")]
        public string VrntPath { get; set; }
    }
}