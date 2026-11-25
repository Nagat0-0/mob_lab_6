using lab_6.Models;
using lab_6.Services;

namespace lab_6.Pages;

public partial class GalleryPage : ContentPage
{
    private readonly GalleryDatabase _database;
    private List<SavedArtwork> _allItems = new();
    private int _sortMode;

    public GalleryPage(GalleryDatabase database)
    {
        InitializeComponent();
        _database = database;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshGalleryAsync();
    }

    private async Task RefreshGalleryAsync()
    {
        _allItems = await _database.GetAllAsync();
        ApplyFilterAndSort();
    }

    private void ApplyFilterAndSort()
    {
        string query = GallerySearchBar.Text?.Trim() ?? string.Empty;
        IEnumerable<SavedArtwork> filtered = _allItems;

        if (!string.IsNullOrEmpty(query))
        {
            filtered = filtered.Where(x =>
                x.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                x.Artist.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                x.Style.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                x.CuratorNote.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        filtered = _sortMode switch
        {
            1 => filtered.OrderBy(x => x.Year),
            2 => filtered.OrderByDescending(x => x.Year),
            _ => filtered.OrderByDescending(x => x.SavedAt)
        };

        var result = filtered.ToList();
        GalleryCollectionView.ItemsSource = result;
        CountLabel.Text = _allItems.Count == 1
            ? "1 artwork collected"
            : $"{_allItems.Count} artworks collected";
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        ApplyFilterAndSort();
    }

    private void OnSortClicked(object? sender, EventArgs e)
    {
        _sortMode = (_sortMode + 1) % 3;
        SortButton.Text = _sortMode switch
        {
            1 => "Sort: Oldest",
            2 => "Sort: Newest",
            _ => "Sort: Recent"
        };
        ApplyFilterAndSort();
    }

    private async void OnArtworkSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not SavedArtwork selected)
            return;

        GalleryCollectionView.SelectedItem = null;
        await Navigation.PushAsync(new ArtworkDetailPage(_database, selected));
    }
}