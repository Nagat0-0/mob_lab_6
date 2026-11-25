using lab_6.Models;
using SQLite;

namespace lab_6.Services;

public class GalleryDatabase
{
    private SQLiteAsyncConnection? _database;

    private async Task InitAsync()
    {
        if (_database != null)
            return;

        string dbPath = Path.Combine(FileSystem.AppDataDirectory, "timecurator.db3");
        _database = new SQLiteAsyncConnection(dbPath);
        await _database.CreateTableAsync<SavedArtwork>();
    }

    public async Task<List<SavedArtwork>> GetAllAsync()
    {
        await InitAsync();
        return await _database!.Table<SavedArtwork>()
            .OrderByDescending(x => x.SavedAt)
            .ToListAsync();
    }

    public async Task<SavedArtwork?> GetByIdAsync(int id)
    {
        await InitAsync();
        return await _database!.Table<SavedArtwork>()
            .Where(x => x.Id == id)
            .FirstOrDefaultAsync();
    }

    public async Task<bool> ExistsAsync(int id)
    {
        await InitAsync();
        int count = await _database!.Table<SavedArtwork>()
            .Where(x => x.Id == id)
            .CountAsync();
        return count > 0;
    }

    public async Task SaveAsync(SavedArtwork artwork)
    {
        await InitAsync();
        await _database!.InsertOrReplaceAsync(artwork);
    }

    public async Task UpdateNoteAsync(int id, string note)
    {
        await InitAsync();
        var item = await GetByIdAsync(id);
        if (item == null)
            return;

        item.CuratorNote = note.Trim();
        await _database!.UpdateAsync(item);
    }

    public async Task DeleteAsync(int id)
    {
        await InitAsync();
        var item = await GetByIdAsync(id);
        if (item != null && !string.IsNullOrEmpty(item.LocalImagePath) && File.Exists(item.LocalImagePath))
        {
            try
            {
                File.Delete(item.LocalImagePath);
            }
            catch
            {
            }
        }
        await _database!.DeleteAsync<SavedArtwork>(id);
    }
}