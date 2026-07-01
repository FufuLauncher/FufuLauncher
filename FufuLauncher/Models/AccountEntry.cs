/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
namespace FufuLauncher.Models;

public class AccountEntry
{
    public string Id
    {
        get; set;
    }           
    public string Stuid
    {
        get; set;
    }
    public string ServerType
    {
        get; set;
    }   
    public string CookieFilePath
    {
        get; set;
    }
    public string Nickname
    {
        get; set;
    }    
    public string AvatarUrl
    {
        get; set;
    }
    public string GameUid
    {
        get; set;
    }
    public DateTime LastLoginTime
    {
        get; set;
    }
}

