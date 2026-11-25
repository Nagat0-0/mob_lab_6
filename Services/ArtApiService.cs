using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using lab_6.Models;

namespace lab_6.Services;

public class ArtApiService
{
    private sealed class ClevelandPagedResponse
    {
        [JsonPropertyName("info")]
        public ClevelandPageInfo? Info { get; set; }

        [JsonPropertyName("data")]
        public List<ClevelandArtItem>? Data { get; set; }
    }

    private sealed class ClevelandPageInfo
    {
        [JsonPropertyName("total")]
        public int Total { get; set; }
    }

    private static readonly string[] PaintingKeywords =
    {
        "oil",
        "tempera",
        "acrylic",
        "canvas",
        "panel"
    };

    private static readonly string[] BlockedKeywords =
    {
        "folio",
        "manuscript",
        "tuti-nama",
        "album",
        "scroll",
        "leaf",
        "codex",
        "page from",
        "calligraphy",
        "illuminated",
        "miniature",
        "book of",
        "screen",
        "handscroll",
        "hanging"
    };

    private readonly HttpClient _httpClient;
    private readonly HashSet<int> _seenIds = new();
    private readonly object _stateLock = new();
    private int _clevelandTotal;

    public ArtApiService()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            MaxConnectionsPerServer = 4
        };
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Mobile Safari/537.36");
    }

    public async Task<List<ArtworkItem>> FetchRandomArtworksAsync(CancellationToken cancellationToken = default)
    {
        string lastError = "Museum archive did not return paintings.";

        try
        {
            var cleveland = await FetchFromClevelandAsync(cancellationToken);
            if (cleveland.Count > 0)
                return cleveland;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            lastError = ex.Message;
        }

        try
        {
            var chicago = await FetchFromChicagoAsync(cancellationToken);
            if (chicago.Count > 0)
                return chicago;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            lastError = ex.Message;
        }

        throw new Exception(lastError);
    }

    private async Task<List<ArtworkItem>> FetchFromClevelandAsync(CancellationToken cancellationToken)
    {
        var collected = new List<ArtworkItem>();

        for (int attempt = 0; attempt < 3; attempt++)
        {
            int maxSkip = _clevelandTotal > 120 ? _clevelandTotal - 80 : 1200;
            int skip = Random.Shared.Next(0, Math.Max(1, maxSkip));
            string url = $"https://openaccess-api.clevelandart.org/api/artworks/?has_image=1&type=Painting&created_after=1400&created_before=2020&limit=80&skip={skip}";

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(12));

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");

            using var response = await _httpClient.SendAsync(request, cts.Token);
            response.EnsureSuccessStatusCode();

            var parsed = await response.Content.ReadFromJsonAsync<ClevelandPagedResponse>(cancellationToken: cts.Token);
            if (parsed?.Info != null && parsed.Info.Total > 120)
            {
                _clevelandTotal = parsed.Info.Total;
            }

            if (parsed?.Data == null)
                continue;

            var filtered = parsed.Data
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item.Title) &&
                    !string.IsNullOrWhiteSpace(item.Images?.Web?.Url) &&
                    item.CreationDateEarliest.HasValue &&
                    item.CreationDateEarliest.Value >= 1400 &&
                    item.CreationDateEarliest.Value <= 2020 &&
                    IsRealPainting(item.Title!, item.Technique, item.Department))
                .Select(item => new ArtworkItem
                {
                    Id = item.Id,
                    Title = item.Title!.Trim(),
                    Artist = string.IsNullOrWhiteSpace(item.Creators?.FirstOrDefault()?.Description)
                        ? "Unknown artist"
                        : item.Creators![0].Description!.Trim(),
                    Year = item.CreationDateEarliest!.Value,
                    DateDisplay = string.IsNullOrWhiteSpace(item.CreationDate)
                        ? item.CreationDateEarliest.Value.ToString()
                        : item.CreationDate.Trim(),
                    Medium = string.IsNullOrWhiteSpace(item.Technique)
                        ? "Oil painting"
                        : item.Technique.Trim(),
                    Origin = string.IsNullOrWhiteSpace(item.Culture?.FirstOrDefault())
                        ? "Unknown origin"
                        : item.Culture![0].Trim(),
                    Style = string.IsNullOrWhiteSpace(item.Department)
                        ? "Painting"
                        : item.Department.Trim(),
                    ImageUrl = item.Images!.Web!.Url!.Trim()
                })
                .ToList();

            collected.AddRange(filtered);
            if (collected.Count >= 12)
                break;
        }

        var shuffled = collected.OrderBy(_ => Random.Shared.Next()).ToList();
        return DiversifyBatch(shuffled);
    }

    private async Task<List<ArtworkItem>> FetchFromChicagoAsync(CancellationToken cancellationToken)
    {
        int page = Random.Shared.Next(1, 100);
        string url = $"https://api.artic.edu/api/v1/artworks?page={page}&limit=50&fields=id,title,artist_display,date_start,date_display,medium_display,place_of_origin,style_title,image_id";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(6));

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("AIC-User-Agent", "TimeCuratorApp/1.0 (student-project@example.com)");

        using var response = await _httpClient.SendAsync(request, cts.Token);
        response.EnsureSuccessStatusCode();

        var parsed = await response.Content.ReadFromJsonAsync<ArtApiResponse>(cancellationToken: cts.Token);
        if (parsed?.Data == null)
            return new List<ArtworkItem>();

        var items = parsed.Data
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.ImageId) &&
                !string.IsNullOrWhiteSpace(item.Title) &&
                item.DateStart.HasValue &&
                item.DateStart.Value >= 1400 &&
                item.DateStart.Value <= 2020 &&
                IsRealPainting(item.Title!, item.MediumDisplay, item.StyleTitle))
            .Select(item => new ArtworkItem
            {
                Id = item.Id,
                Title = item.Title!.Trim(),
                Artist = string.IsNullOrWhiteSpace(item.ArtistDisplay) ? "Unknown artist" : item.ArtistDisplay.Trim(),
                Year = item.DateStart!.Value,
                DateDisplay = string.IsNullOrWhiteSpace(item.DateDisplay) ? item.DateStart.Value.ToString() : item.DateDisplay.Trim(),
                Medium = string.IsNullOrWhiteSpace(item.MediumDisplay) ? "Oil painting" : item.MediumDisplay.Trim(),
                Origin = string.IsNullOrWhiteSpace(item.PlaceOfOrigin) ? "Unknown origin" : item.PlaceOfOrigin.Trim(),
                Style = string.IsNullOrWhiteSpace(item.StyleTitle) ? "Painting" : item.StyleTitle.Trim(),
                ImageUrl = $"https://www.artic.edu/iiif/2/{item.ImageId}/full/843,/0/default.jpg"
            })
            .OrderBy(_ => Random.Shared.Next())
            .ToList();

        return DiversifyBatch(items);
    }

    private static bool IsRealPainting(string title, string? technique, string? department)
    {
        if (string.IsNullOrWhiteSpace(technique))
            return false;

        bool matchesMedium = PaintingKeywords.Any(k => technique.Contains(k, StringComparison.OrdinalIgnoreCase));
        if (!matchesMedium)
            return false;

        string combined = $"{title} {technique} {department}".ToLowerInvariant();
        return !BlockedKeywords.Any(keyword => combined.Contains(keyword, StringComparison.Ordinal));
    }

    private List<ArtworkItem> DiversifyBatch(List<ArtworkItem> candidates)
    {
        var result = new List<ArtworkItem>();
        var usedArtists = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int unknownArtistCount = 0;

        lock (_stateLock)
        {
            foreach (var item in candidates)
            {
                if (_seenIds.Contains(item.Id))
                    continue;

                string artistKey = item.Artist.Split('\n').FirstOrDefault()?.Trim() ?? "Unknown artist";
                bool isUnknown = artistKey.Equals("Unknown artist", StringComparison.OrdinalIgnoreCase);

                if (isUnknown)
                {
                    if (unknownArtistCount >= 2)
                        continue;
                    unknownArtistCount++;
                }
                else if (!usedArtists.Add(artistKey))
                {
                    continue;
                }

                _seenIds.Add(item.Id);
                result.Add(item);

                if (result.Count >= 20)
                    break;
            }

            if (result.Count == 0 && candidates.Count > 0)
            {
                _seenIds.Clear();
                return candidates.Take(15).ToList();
            }
        }

        return result;
    }

    public async Task<byte[]?> DownloadImageBytesAsync(string imageUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(12));

            using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
            request.Headers.TryAddWithoutValidation("Accept", "image/*");

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadAsByteArrayAsync(cts.Token);
        }
        catch
        {
            return null;
        }
    }
}