using System.Text.Json.Serialization;

namespace lab_6.Models;

public class ClevelandApiResponse
{
    [JsonPropertyName("data")]
    public List<ClevelandArtItem>? Data { get; set; }
}

public class ClevelandArtItem
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("creation_date")]
    public string? CreationDate { get; set; }

    [JsonPropertyName("creation_date_earliest")]
    public int? CreationDateEarliest { get; set; }

    [JsonPropertyName("technique")]
    public string? Technique { get; set; }

    [JsonPropertyName("department")]
    public string? Department { get; set; }

    [JsonPropertyName("culture")]
    public List<string>? Culture { get; set; }

    [JsonPropertyName("creators")]
    public List<ClevelandCreator>? Creators { get; set; }

    [JsonPropertyName("images")]
    public ClevelandImages? Images { get; set; }
}

public class ClevelandCreator
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public class ClevelandImages
{
    [JsonPropertyName("web")]
    public ClevelandImageFile? Web { get; set; }
}

public class ClevelandImageFile
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public class ArtApiResponse
{
    [JsonPropertyName("data")]
    public List<ArtApiItem>? Data { get; set; }
}

public class ArtApiItem
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("artist_display")]
    public string? ArtistDisplay { get; set; }

    [JsonPropertyName("date_start")]
    public int? DateStart { get; set; }

    [JsonPropertyName("date_display")]
    public string? DateDisplay { get; set; }

    [JsonPropertyName("medium_display")]
    public string? MediumDisplay { get; set; }

    [JsonPropertyName("place_of_origin")]
    public string? PlaceOfOrigin { get; set; }

    [JsonPropertyName("style_title")]
    public string? StyleTitle { get; set; }

    [JsonPropertyName("image_id")]
    public string? ImageId { get; set; }
}

public class ArtworkItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public int Year { get; set; }
    public string DateDisplay { get; set; } = string.Empty;
    public string Medium { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public string Style { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
}