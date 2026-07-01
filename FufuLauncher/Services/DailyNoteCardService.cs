/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
// Copyright (c) FufuLauncher Dev Team. All rights reserved.
// By kyxsan.
// Licensed under the MIT License.

using FufuLauncher.Services.MiHoYo;

namespace FufuLauncher.Services;

public class DailyNoteCardService
{
    private readonly DailyNoteService _dailyNoteService = new();

    public async Task<DailyNoteCardData> LoadCardDataAsync(string roleId, string server, Dictionary<string, string> cookies)
    {
        return await _dailyNoteService.GetDailyNoteAsync(roleId, server);
    }
}
