using System.Diagnostics;
using FufuLauncher.Views;

namespace FufuLauncher.Services
{
    public class DailyNoteCardService
    {
        public async Task<DailyNoteCardData> LoadCardDataAsync(string roleId, string server)
        {
            var json = await FetchApiJson(roleId, server);
            return DailyNoteParser.Parse(json);
        }

        private async Task<string> FetchApiJson(string roleId, string server)
        {
            var apiUrl = $"https://api-takumi-record.mihoyo.com/game_record/app/genshin/api/dailyNote?server={server}&role_id={roleId}";
            Debug.WriteLine($"[DailyNoteCardService] 请求API: {apiUrl}");
            return await BBSWindow.FetchApiJsonAsync(apiUrl);
        }
    }
}
