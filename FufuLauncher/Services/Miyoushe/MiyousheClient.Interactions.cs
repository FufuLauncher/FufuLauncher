using System.Text.Json;
using FufuLauncher.Helpers;
using FufuLauncher.Models.Miyoushe;

namespace FufuLauncher.Services.Miyoushe;

public sealed class CommunitySubmissionException(Exception inner) : Exception("Miyoushe_SubmissionUncertainIO".GetLocalized(), inner);

public sealed partial class MiyousheClient
{
    public void RequireInteractionAccount()
    {
        RequireLogin("ltoken", "ltoken_v2", "cookie_token", "cookie_token_v2");
        if (AccountUid.Length == 0) throw new CommunityApiException("Miyoushe_ReloginRequired".GetLocalized(), -100, loginExpired: true);
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
            throw new ArgumentException("Miyoushe_CommentLengthLimit".GetLocalized());
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