namespace FufuLauncher.Contracts.Services;

//获取指纹
public interface IDeviceFingerprintService
{
    // 获取或注册设备指纹，按 accountId 来
    Task<string> GetOrRegisterFingerprintAsync(string accountId, Dictionary<string, string> cookies);

    // 获取当前活跃账号的指纹
    string? GetCurrentFingerprint(string accountId);

    //强制重新注册指纹
    Task ResetFingerprintAsync(string accountId);
}
