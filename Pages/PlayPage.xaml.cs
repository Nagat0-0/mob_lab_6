using System.Collections.Concurrent;
using lab_6.Models;
using lab_6.Services;

namespace lab_6.Pages;

public partial class PlayPage : ContentPage
{
    private enum GameDifficulty
    {
        Easy,
        Medium,
        Hard
    }

    private sealed record PreparedArtwork(ArtworkItem Artwork, byte[] ImageBytes);

    private const int BufferSize = 5;
    private const int MaxWorkers = 2;

    private readonly ArtApiService _apiService;
    private readonly GalleryDatabase _database;
    private readonly ConcurrentQueue<ArtworkItem> _metadataQueue = new();
    private readonly ConcurrentQueue<PreparedArtwork> _readyQueue = new();
    private readonly SemaphoreSlim _metadataSemaphore = new(1, 1);
    private readonly SemaphoreSlim _readySignal = new(0, 1);
    private readonly object _workerLock = new();

    private CancellationTokenSource _pipelineCts = new();
    private ArtworkItem? _currentArtwork;
    private byte[]? _currentImageBytes;
    private GameDifficulty _difficulty = GameDifficulty.Medium;
    private bool _isInitialized;
    private bool _isLoading;
    private int _pipelineVersion;
    private int _activeWorkers;
    private string? _lastError;
    private int _score;
    private int _streak;
    private int _bestScore;

    public PlayPage(ArtApiService apiService, GalleryDatabase database)
    {
        InitializeComponent();
        _apiService = apiService;
        _database = database;
        _bestScore = Preferences.Default.Get("best_score", 0);
        UpdateStatsUi();
        UpdateDifficultyUi();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (!_isInitialized)
        {
            _isInitialized = true;
            await LoadNextArtworkAsync();
        }
        else if (_currentArtwork != null && ResultPanel.IsVisible)
        {
            bool saved = await _database.ExistsAsync(_currentArtwork.Id);
            UpdateSaveButton(saved);
        }
    }

    private void OnDifficultyClicked(object? sender, EventArgs e)
    {
        if (sender is not Button btn)
            return;

        GameDifficulty next = btn.CommandParameter?.ToString() switch
        {
            "Easy" => GameDifficulty.Easy,
            "Hard" => GameDifficulty.Hard,
            _ => GameDifficulty.Medium
        };

        if (next == _difficulty)
            return;

        _difficulty = next;
        _score = 0;
        _streak = 0;
        UpdateStatsUi();
        UpdateDifficultyUi();
    }

    private int GetTolerance() => _difficulty switch
    {
        GameDifficulty.Easy => 100,
        GameDifficulty.Hard => 25,
        _ => 50
    };

    private int CalculatePoints(int diff) => _difficulty switch
    {
        GameDifficulty.Easy => Math.Max(100, 1000 - diff * 8),
        GameDifficulty.Hard => Math.Max(250, 2000 - diff * 50),
        _ => Math.Max(150, 1500 - diff * 20)
    };

    private void UpdateDifficultyUi()
    {
        StyleDifficultyButton(EasyButton, _difficulty == GameDifficulty.Easy, "#34D399");
        StyleDifficultyButton(MediumButton, _difficulty == GameDifficulty.Medium, "#E2B75B");
        StyleDifficultyButton(HardButton, _difficulty == GameDifficulty.Hard, "#F87171");

        DifficultyInfoLabel.Text = _difficulty switch
        {
            GameDifficulty.Easy => "Limit: ±100 years • Miss resets score • Max 1000 pts",
            GameDifficulty.Hard => "Limit: ±25 years • Miss resets score • Max 2000 pts",
            _ => "Limit: ±50 years • Miss resets score • Max 1500 pts"
        };
    }

    private void StyleDifficultyButton(Button button, bool active, string accentHex)
    {
        if (active)
        {
            button.BackgroundColor = Color.FromArgb(accentHex);
            button.TextColor = Color.FromArgb("#0D0F17");
            button.BorderWidth = 0;
        }
        else
        {
            button.BackgroundColor = Color.FromArgb("#171A26");
            button.TextColor = Color.FromArgb("#94A3B8");
            button.BorderColor = Color.FromArgb("#262B3D");
            button.BorderWidth = 1;
        }
    }

    private void SignalReady()
    {
        if (_readySignal.CurrentCount == 0)
        {
            try
            {
                _readySignal.Release();
            }
            catch (SemaphoreFullException)
            {
            }
        }
    }

    private void ResetPipeline()
    {
        lock (_workerLock)
        {
            _pipelineCts.Cancel();
            _pipelineCts.Dispose();
            _pipelineCts = new CancellationTokenSource();
            _pipelineVersion++;
            _activeWorkers = 0;
            _lastError = null;
        }
    }

    private void EnsureBufferFilled()
    {
        lock (_workerLock)
        {
            if (_lastError != null && _metadataQueue.IsEmpty)
                return;

            var token = _pipelineCts.Token;
            int version = _pipelineVersion;

            while (_activeWorkers < MaxWorkers && (_readyQueue.Count + _activeWorkers) < BufferSize)
            {
                _activeWorkers++;
                _ = Task.Run(() => WorkerLoopAsync(version, token));
            }
        }
    }

    private async Task WorkerLoopAsync(int version, CancellationToken token)
    {
        int failedDownloads = 0;

        try
        {
            while (!token.IsCancellationRequested && _readyQueue.Count < BufferSize)
            {
                var candidate = await DequeueMetadataAsync(token);
                if (candidate == null)
                    break;

                var bytes = await _apiService.DownloadImageBytesAsync(candidate.ImageUrl, token);
                if (token.IsCancellationRequested)
                    break;

                if (bytes == null || bytes.Length < 1000)
                {
                    failedDownloads++;
                    if (failedDownloads >= 8)
                    {
                        _lastError = "Image server is slow or unreachable.";
                        break;
                    }
                    continue;
                }

                failedDownloads = 0;
                _readyQueue.Enqueue(new PreparedArtwork(candidate, bytes));
                SignalReady();
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            lock (_workerLock)
            {
                if (version == _pipelineVersion && _activeWorkers > 0)
                {
                    _activeWorkers--;
                }
            }
            SignalReady();
        }
    }

    private async Task<ArtworkItem?> DequeueMetadataAsync(CancellationToken token)
    {
        if (_metadataQueue.TryDequeue(out var item))
            return item;

        await _metadataSemaphore.WaitAsync(token);
        try
        {
            if (_metadataQueue.TryDequeue(out item))
                return item;

            if (_lastError != null)
                return null;

            try
            {
                var batch = await _apiService.FetchRandomArtworksAsync(token);
                foreach (var artwork in batch)
                {
                    _metadataQueue.Enqueue(artwork);
                }
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return null;
            }

            return _metadataQueue.TryDequeue(out item) ? item : null;
        }
        finally
        {
            _metadataSemaphore.Release();
        }
    }

    private void DisplayPreparedArtwork(PreparedArtwork prepared)
    {
        _currentArtwork = prepared.Artwork;
        _currentImageBytes = prepared.ImageBytes;
        ArtworkImage.Source = ImageSource.FromStream(() => new MemoryStream(prepared.ImageBytes));
        TitleLabel.Text = prepared.Artwork.Title;
        YearSlider.Value = 1850;
        SelectedYearLabel.Text = "1850";
        ErrorPanel.IsVisible = false;
        ResultPanel.IsVisible = false;
        GuessPanel.IsVisible = true;
        GuessButton.IsEnabled = true;
        ZoomBadge.IsVisible = true;
        SetLoadingState(false);
    }

    private async Task LoadNextArtworkAsync()
    {
        if (_isLoading)
            return;

        if (_readyQueue.TryDequeue(out var cached))
        {
            DisplayPreparedArtwork(cached);
            EnsureBufferFilled();
            return;
        }

        _isLoading = true;
        _lastError = null;
        SetLoadingState(true);
        ErrorPanel.IsVisible = false;
        ResultPanel.IsVisible = false;
        GuessPanel.IsVisible = true;
        GuessButton.IsEnabled = false;
        ZoomBadge.IsVisible = false;
        ArtworkImage.Source = null;
        TitleLabel.Text = "Curating next painting...";

        EnsureBufferFilled();

        try
        {
            var startedAt = DateTime.UtcNow;

            while (true)
            {
                if (_readyQueue.TryDequeue(out var prepared))
                {
                    DisplayPreparedArtwork(prepared);
                    EnsureBufferFilled();
                    return;
                }

                bool noWorkers;
                lock (_workerLock)
                {
                    noWorkers = _activeWorkers == 0;
                }

                if (noWorkers)
                {
                    string message = _lastError != null
                        ? $"API error: {_lastError}"
                        : "Could not download painting image. Tap Retry.";
                    ShowError(message);
                    return;
                }

                if ((DateTime.UtcNow - startedAt).TotalSeconds > 25)
                {
                    ResetPipeline();
                    ShowError("Loading timed out. Tap Retry.");
                    return;
                }

                await _readySignal.WaitAsync(250);
            }
        }
        finally
        {
            SetLoadingState(false);
            _isLoading = false;
        }
    }

    private void SetLoadingState(bool loading)
    {
        LoadingIndicator.IsVisible = loading;
        LoadingIndicator.IsRunning = loading;
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorPanel.IsVisible = true;
        TitleLabel.Text = "Archive unavailable";
        GuessButton.IsEnabled = false;
    }

    private void OnYearSliderChanged(object? sender, ValueChangedEventArgs e)
    {
        int year = (int)Math.Round(e.NewValue);
        SelectedYearLabel.Text = year.ToString();
    }

    private void OnAdjustYearClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && int.TryParse(btn.CommandParameter?.ToString(), out int delta))
        {
            double nextValue = Math.Clamp(Math.Round(YearSlider.Value) + delta, YearSlider.Minimum, YearSlider.Maximum);
            YearSlider.Value = nextValue;
        }
    }

    private async void OnGuessClicked(object? sender, EventArgs e)
    {
        if (_currentArtwork == null)
            return;

        int guessedYear = (int)Math.Round(YearSlider.Value);
        int actualYear = _currentArtwork.Year;
        int diff = Math.Abs(actualYear - guessedYear);
        int tolerance = GetTolerance();

        if (diff <= tolerance)
        {
            int earned = CalculatePoints(diff);
            _score += earned;
            _streak++;

            if (_score > _bestScore)
            {
                _bestScore = _score;
                Preferences.Default.Set("best_score", _bestScore);
            }

            if (diff <= Math.Max(5, tolerance / 4))
            {
                ResultVerdictLabel.Text = "MASTERPIECE ACCURACY!";
                ResultVerdictLabel.TextColor = Color.FromArgb("#34D399");
            }
            else
            {
                ResultVerdictLabel.Text = "GREAT CURATOR EYE!";
                ResultVerdictLabel.TextColor = Color.FromArgb("#E2B75B");
            }

            ResultPointsBorder.BackgroundColor = Color.FromArgb("#1D2B24");
            ResultPointsLabel.TextColor = Color.FromArgb("#34D399");
            ResultPointsLabel.Text = $"+{earned} pts";
        }
        else
        {
            _score = 0;
            _streak = 0;

            ResultVerdictLabel.Text = "MISSED! SCORE RESET";
            ResultVerdictLabel.TextColor = Color.FromArgb("#F87171");
            ResultPointsBorder.BackgroundColor = Color.FromArgb("#2B1D24");
            ResultPointsLabel.TextColor = Color.FromArgb("#F87171");
            ResultPointsLabel.Text = "RESET";
        }

        UpdateStatsUi();

        ResultDiffLabel.Text = $"Actual: {actualYear} • Guess: {guessedYear} (off by {diff}y, limit ±{tolerance}y)";

        string shortArtist = _currentArtwork.Artist.Split('\n').FirstOrDefault()?.Trim() ?? "Unknown artist";
        RevealArtistLabel.Text = shortArtist;
        RevealMetaLabel.Text = $"{_currentArtwork.DateDisplay} • {_currentArtwork.Origin} • {_currentArtwork.Style}";
        RevealMediumLabel.Text = _currentArtwork.Medium;

        bool alreadySaved = await _database.ExistsAsync(_currentArtwork.Id);
        UpdateSaveButton(alreadySaved);

        GuessPanel.IsVisible = false;
        ResultPanel.IsVisible = true;
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (_currentArtwork == null)
            return;

        string localPath = string.Empty;
        if (_currentImageBytes != null && _currentImageBytes.Length > 0)
        {
            localPath = Path.Combine(FileSystem.AppDataDirectory, $"art_{_currentArtwork.Id}.jpg");
            await File.WriteAllBytesAsync(localPath, _currentImageBytes);
        }

        var entity = new SavedArtwork
        {
            Id = _currentArtwork.Id,
            Title = _currentArtwork.Title,
            Artist = _currentArtwork.Artist,
            Year = _currentArtwork.Year,
            DateDisplay = _currentArtwork.DateDisplay,
            Medium = _currentArtwork.Medium,
            Origin = _currentArtwork.Origin,
            Style = _currentArtwork.Style,
            ImageUrl = _currentArtwork.ImageUrl,
            LocalImagePath = localPath,
            CuratorNote = string.Empty,
            SavedAt = DateTime.UtcNow
        };

        await _database.SaveAsync(entity);
        UpdateSaveButton(true);
    }

    private void UpdateSaveButton(bool isSaved)
    {
        if (isSaved)
        {
            SaveButton.Text = "Saved ✓";
            SaveButton.IsEnabled = false;
            SaveButton.BackgroundColor = Color.FromArgb("#1D2B24");
            SaveButton.TextColor = Color.FromArgb("#34D399");
        }
        else
        {
            SaveButton.Text = "Save to Gallery";
            SaveButton.IsEnabled = true;
            SaveButton.BackgroundColor = Color.FromArgb("#1F2437");
            SaveButton.TextColor = Color.FromArgb("#E2B75B");
        }
    }

    private async void OnNextClicked(object? sender, EventArgs e)
    {
        await LoadNextArtworkAsync();
    }

    private async void OnRetryClicked(object? sender, EventArgs e)
    {
        ResetPipeline();
        await LoadNextArtworkAsync();
    }

    private async void OnArtworkImageTapped(object? sender, TappedEventArgs e)
    {
        if (_currentArtwork == null || _currentImageBytes == null || _currentImageBytes.Length == 0)
            return;

        var source = ImageSource.FromStream(() => new MemoryStream(_currentImageBytes));
        await Navigation.PushModalAsync(new ImageViewerPage(source, _currentArtwork.Title));
    }

    private void UpdateStatsUi()
    {
        ScoreLabel.Text = _score.ToString();
        StreakLabel.Text = _streak.ToString();
        BestLabel.Text = _bestScore.ToString();
    }
}