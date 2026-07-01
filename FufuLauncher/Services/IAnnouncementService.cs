/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
namespace FufuLauncher.Contracts.Services;

public interface IAnnouncementService
{
    Task<string?> CheckForNewAnnouncementAsync();
    
    Task<string> GetCurrentAnnouncementUrlAsync();
}
