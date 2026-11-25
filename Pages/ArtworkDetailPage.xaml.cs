using lab_6.Models;
using lab_6.Services;

namespace lab_6.Pages;

public partial class ArtworkDetailPage : ContentPage
{
    private readonly GalleryDatabase _database;
    private readonly SavedArtwork _artwork;
    private bool _confirmDelete;

    public ArtworkDetailPage(GalleryDatabase database, SavedArtwork artwork)
    {
        InitializeComponent();
        _database = database;
        _artwork = artwork;

        DetailImage.Source = !string.IsNullOrEmpty(artwork.LocalImagePath) && File.Exists(artwork.LocalImagePath)
            ? ImageSource.FromFile(artwork.LocalImagePath)
            : artwork.ImageUrl;

        DetailTitleLabel.Text = artwork.Title;
        DetailArtistLabel.Text = artwork.Artist;
        DetailDateLabel.Text = $"Year: {artwork.DateDisplay}";
        DetailStyleLabel.Text = $"Style: {artwork.Style}";
        DetailOriginLabel.Text = $"Origin: {artwork.Origin}";
        DetailMediumLabel.Text = $"Medium: {artwork.Medium}";
        NoteEditor.Text = artwork.CuratorNote;
    }

    private async void OnSaveNoteClicked(object? sender, EventArgs e)
    {
        string text = NoteEditor.Text ?? string.Empty;
        await _database.UpdateNoteAsync(_artwork.Id, text);
        _artwork.CuratorNote = text.Trim();
        NoteStatusLabel.Text = "Note saved ✓";
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (!_confirmDelete)
        {
            _confirmDelete = true;
            DeleteButton.Text = "Tap again to confirm removal";
            DeleteButton.BackgroundColor = Color.FromArgb("#7F1D1D");
            DeleteButton.TextColor = Colors.White;
            return;
        }

        await _database.DeleteAsync(_artwork.Id);
        await Navigation.PopAsync();
    }

    private async void OnDetailImageTapped(object? sender, TappedEventArgs e)
    {
        ImageSource source = !string.IsNullOrEmpty(_artwork.LocalImagePath) && File.Exists(_artwork.LocalImagePath)
            ? ImageSource.FromFile(_artwork.LocalImagePath)
            : ImageSource.FromUri(new Uri(_artwork.ImageUrl));

        await Navigation.PushModalAsync(new ImageViewerPage(source, _artwork.Title));
    }
}