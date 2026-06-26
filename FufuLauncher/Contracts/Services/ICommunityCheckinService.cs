/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using FufuLauncher.Models;

namespace FufuLauncher.Contracts.Services;

public interface ICommunityCheckinService
{
    Task<CheckinTypeResult> ExecuteCheckinAsync(AccountCredentials account, bool signEnabled, bool readEnabled, bool likeEnabled, bool shareEnabled);
}

