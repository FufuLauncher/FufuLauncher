using System.Text.Json;
using FufuLauncher.Helpers;
using FufuLauncher.Models.Miyoushe;

namespace FufuLauncher.Services.Miyoushe;

public sealed class CommunityLibrary(string path)
{
    private sealed record LibraryData(List<CommunityPost> Bookmarks, List<CommunityPost> History);

    private List<CommunityPost> _bookmarks = [];
    private List<CommunityPost> _history = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _loadFailed;
    public IReadOnlyList<CommunityPost> Bookmarks => _bookmarks.ToArray();
    public IReadOnlyList<CommunityPost> History => _history.ToArray();
    public bool Contains(string id) => _bookmarks.Any(p => p.Id == id);

    public async Task LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(path)) return;
        try
        {
            await using var file = File.OpenRead(path);
            var data = await JsonSerializer.DeserializeAsync<LibraryData>(file, cancellationToken: ct);
            _bookmarks = data?.Bookmarks ?? [];
            _history = data?.History ?? [];
            _loadFailed = false;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _loadFailed = true;
            throw;
        }
    }

    public Task ToggleAsync(CommunityPost post, CancellationToken ct) => UpdateAsync(() =>
    {
        if (Contains(post.Id)) _bookmarks.RemoveAll(p => p.Id == post.Id);
        else _bookmarks.Insert(0, post with { Raw = default });
    }, ct);

    public Task VisitAsync(CommunityPost post, CancellationToken ct) => UpdateAsync(() =>
    {
        _history.RemoveAll(p => p.Id == post.Id);
        _history.Insert(0, post with { Raw = default });
        if (_history.Count > 200) _history.RemoveRange(200, _history.Count - 200);
    }, ct);

    private async Task UpdateAsync(Action update, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        var previousBookmarks = _bookmarks.ToList();
        var previousHistory = _history.ToList();
        try
        {
            if (_loadFailed) throw new IOException("Miyoushe_LibraryLoadFailedIO".GetLocalized());
            update();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            await using (var file = File.Create(temp))
                await JsonSerializer.SerializeAsync(file, new LibraryData(_bookmarks, _history), cancellationToken: ct);
            File.Move(temp, path, true);
        }
        catch
        {
            _bookmarks = previousBookmarks;
            _history = previousHistory;
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }
}