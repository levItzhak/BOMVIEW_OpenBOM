using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace BOMVIEW.OpenBOM.Models
{
    public class OpenBomAuthResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; }
    }

    public class OpenBomDocument
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("columns")]
        public List<string> Columns { get; set; }

        [JsonPropertyName("cells")]
        public List<List<object>> Cells { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }
    }

    public class OpenBomUploadResult
    {
        public int TotalParts { get; set; }
        public int SuccessfulUploads { get; set; }
        public int SkippedParts { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> SuccessfulPartNumbers { get; set; } = new List<string>();
        public List<string> SkippedPartNumbers { get; set; } = new List<string>();
    }

    public class OpenBomPartRequest
    {
        [JsonPropertyName("partNumber")]
        public string PartNumber { get; set; }

        [JsonPropertyName("properties")]
        public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();
    }

    public class OpenBomListItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("bomId")]
        public string BomId { get; set; }

        [JsonPropertyName("partNumber")]
        public string PartNumber { get; set; }

        public bool MatchesSearch(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
                return true;

            searchTerm = searchTerm.ToLower();
            return Name?.ToLower().Contains(searchTerm) == true ||
                   Id?.ToLower().Contains(searchTerm) == true ||
                   BomId?.ToLower().Contains(searchTerm) == true ||
                   PartNumber?.ToLower().Contains(searchTerm) == true;
        }

        public override string ToString()
        {
            return $"{Name} (ID: {BomId ?? Id})";
        }
    }
}