using SQLite;

namespace lab_6.Models;

[Table("saved_artworks")]
public class SavedArtwork
{
    [PrimaryKey]
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public int Year { get; set; }
    public string DateDisplay { get; set; } = string.Empty;
    public string Medium { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public string Style { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string LocalImagePath { get; set; } = string.Empty;
    public string CuratorNote { get; set; } = string.Empty;
    public DateTime SavedAt { get; set; }

    [Ignore]
    public string DisplayImage => !string.IsNullOrEmpty(LocalImagePath) && File.Exists(LocalImagePath)
        ? LocalImagePath
        : ImageUrl;

    [Ignore]
    public string ShortArtist => Artist.Split('\n').FirstOrDefault()?.Trim() ?? "Unknown artist";

    [Ignore]
    public string MetaLine => $"{Year} • {ShortArtist}";
}