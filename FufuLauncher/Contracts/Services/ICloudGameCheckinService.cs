/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using FufuLauncher.Models;

namespace FufuLauncher.Contracts.Services;

public interface ICloudGameCheckinService
{
    Task<CheckinTypeResult> ExecuteCheckinAsync(string uid, string comboToken);
}

