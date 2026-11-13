using System.Text.Json;
using FufuLauncher.Models.Miyoushe;

namespace FufuLauncher.Services.Miyoushe;

public sealed class CommunitySubmissionException(Exception inner) : Exception("评论提交结果暂时无法确认，请先检查原帖，避免重复发布", inner);

public sealed partial class MiyousheClient
{
    public void RequireInteractionAccount()
    {
        RequireLogin("ltoken", "ltoken_v2", "cookie_token", "cookie_token_v2");
        if (AccountUid.Length == 0) throw new CommunityApiException("请重新登录米游社账号", -100, loginExpired: true);
    }

    public async Task SetPostLikeAsync(string postId, bool liked, CancellationToken ct)
    {
        RequireInteractionAccount();
        ValidateId(postId);
        string body = JsonSerializer.Serialize(new { is_cancel = !liked, post_id = postId });
        await SendAsync(ApiEndpoints.MiyousheUpvotePostPath, [], Profile.K2Sign, ct, body);
    }

    public async Task SetReplyLikeAsync(string postId, string replyId, bool liked, CancellationToken ct)
    {
        RequireInteractionAccount();
        ValidateId(postId);
        ValidateId(replyId);
        string body = JsonSerializer.Serialize(new { is_cancel = !liked, post_id = postId, reply_id = replyId });
        await SendAsync(ApiEndpoints.MiyousheUpvoteReplyPath, [], Profile.K2Sign, ct, body);
    }

    public async Task PublishReplyAsync(CommunityPost post, string content, string? replyId, CancellationToken ct)
    {
        RequireInteractionAccount();
        ValidateId(post.Id);
        if (replyId != null) ValidateId(replyId);
        if (string.IsNullOrWhiteSpace(content) || content.Length > 1000)
            throw new ArgumentException("评论需包含 1 至 1000 个字符");
        content = content.Replace("\r\n", "\n").Replace("\r", "\n");
        var body = new Dictionary<string, object>
        {
            ["gids"] = post.GameId.ToString(), ["post_id"] = post.Id, ["content"] = content,
            ["structured_content"] = JsonSerializer.Serialize(new[] { new { insert = content + "\n" } })
        };
        if (replyId != null) body["reply_id"] = replyId;
        await SendAsync(ApiEndpoints.MiyousheReleaseReplyPath, [], Profile.WebSign, ct, JsonSerializer.Serialize(body),
            submission: true);
    }
}