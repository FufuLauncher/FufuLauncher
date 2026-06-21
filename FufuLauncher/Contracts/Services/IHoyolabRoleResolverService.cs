namespace FufuLauncher.Contracts.Services;

public record HoyolabRoleResolveResult(
    int RetCode,
    string Message,
    string Source,
    List<GameRoleInfo> Roles)
{
    public bool HasRoles => Roles.Count > 0;
}

public interface IHoyolabRoleResolverService
{
    Task<HoyolabRoleResolveResult> ResolveRolesAsync(string cookie, CancellationToken cancellationToken = default);
}
