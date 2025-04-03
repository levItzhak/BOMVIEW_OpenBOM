using System.Text.Json.Serialization;

namespace BOMVIEW.Models
{
    public class DigiKeyListRequest
    {
        [JsonPropertyName("ListName")]
        public string ListName { get; set; }

        [JsonPropertyName("CreatedBy")]
        public string CreatedBy { get; set; }

        [JsonPropertyName("Tags")]
        public string[] Tags { get; set; } = new string[] { };

        [JsonPropertyName("Source")]
        public string Source { get; set; } = "other";

        [JsonPropertyName("ListSettings")]
        public DigiKeyListSettings ListSettings { get; set; }
    }

    public class DigiKeyListSettings
    {
        [JsonPropertyName("InternalOnly")]
        public bool InternalOnly { get; set; } = false;

        [JsonPropertyName("Visibility")]
        public string Visibility { get; set; } = "Private";

        [JsonPropertyName("PackagePreference")]
        public string PackagePreference { get; set; } = "CutTapeOrTR";

        [JsonPropertyName("ColumnPreferences")]
        public DigiKeyColumnPreference[] ColumnPreferences { get; set; }

        [JsonPropertyName("AutoCorrectQuantities")]
        public bool AutoCorrectQuantities { get; set; }

        [JsonPropertyName("AttritionEnabled")]
        public bool AttritionEnabled { get; set; }

        [JsonPropertyName("AutoPopulateCref")]
        public bool AutoPopulateCref { get; set; }
    }

    public class DigiKeyColumnPreference
    {
        [JsonPropertyName("ColumnName")]
        public string ColumnName { get; set; }

        [JsonPropertyName("IsVisible")]
        public bool IsVisible { get; set; }
    }

    public class DigiKeyListResponse
    {
        [JsonPropertyName("ListId")]
        public string ListId { get; set; }

        [JsonPropertyName("Success")]
        public bool Success { get; set; }

        [JsonPropertyName("ErrorMessage")]
        public string ErrorMessage { get; set; }
    }
}