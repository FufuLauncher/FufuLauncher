using FufuLauncher.Models.Genshin;

namespace FufuLauncher.Services;

public interface IGenshinService
{
    Task<TravelersDiarySummary> GetTravelersDiarySummaryAsync(string uid, string cookie, int month = 0, CancellationToken cancellationToken = default);
}

public class GenshinService : IGenshinService
{
    private readonly GenshinApiClient _client;

    public GenshinService()
    {
        _client = new GenshinApiClient();
    }

    public async Task<TravelersDiarySummary> GetTravelersDiarySummaryAsync(string uid, string cookie, int month = 0, CancellationToken cancellationToken = default)
    {
        return await _client.GetTravelersDiarySummaryAsync(uid, cookie, month, cancellationToken);
    }
}