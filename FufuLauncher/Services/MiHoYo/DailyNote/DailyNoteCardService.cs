/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

namespace FufuLauncher.Services.MiHoYo.DailyNote;

public class DailyNoteCardService
{
    private readonly DailyNoteService _dailyNoteService = new();

    public async Task<DailyNoteCardData> LoadCardDataAsync(string roleId, string server, string? accountId = null)
    {
        return await _dailyNoteService.GetDailyNoteAsync(roleId, server, accountId);
    }
}