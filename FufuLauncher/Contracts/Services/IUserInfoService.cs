namespace FufuLauncher.Contracts.Services;

public record GameRoleInfo(
    string game_biz,
    string region,
    string game_uid,
    string nickname,
    int level,
    string region_name
);

public record GameRolesData(List<GameRoleInfo> list);
public record GameRolesResponse(int retcode, string message, GameRolesData? data);

public record UserInfo(
    string uid,
    string nickname,
    string introduce,
    string avatar_url,
    Dictionary<string, string> achieve
);

public record UserFullInfoData(UserInfo user_info);
public record UserFullInfoResponse(int retcode, string message, UserFullInfoData? data);

public record GameDataItem(string name, int type, string value);
public record GameRecordCardInfo(
    string game_role_id,
    string nickname,
    string region_name,
    int level,
    List<GameDataItem> data
);

public record GameRecordCardData(List<GameRecordCardInfo> list);
public record GameRecordCardResponse(int retcode, string message, GameRecordCardData? data);


public interface IUserInfoService
{
    Task<GameRolesResponse> GetUserGameRolesAsync(string cookie);
    Task<UserFullInfoResponse> GetUserFullInfoAsync(string cookie);
    Task<GameRecordCardResponse> GetGameRecordCardAsync(string stuid, string cookie);

    Task SaveUserDataAsync(string cookie, string stuid);
}