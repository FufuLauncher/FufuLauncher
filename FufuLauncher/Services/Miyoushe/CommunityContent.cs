using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FufuLauncher.Models.Miyoushe;
using HtmlAgilityPack;

namespace FufuLauncher.Services.Miyoushe;

public static partial class CommunityContent
{
    private static readonly Regex BareUrl = new(@"https?://[A-Za-z0-9\-._~:/?#\[\]@!$&()*+,;=%]+",
        RegexOptions.Compiled);

    private static readonly HashSet<string> Tags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "div", "span", "strong", "b", "em", "i", "u", "s", "blockquote", "pre", "code", "ul", "ol", "li",
        "h1", "h2", "h3", "h4", "hr", "a", "img", "table", "tbody", "thead", "tr", "td", "th", "sub", "sup"
    };

    public static string ToDocumentUri(string html) =>
        "data:text/html;charset=utf-8;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(html));

    public static bool TryWebUri(string? value, out Uri uri)
    {
        uri = null!;
        return Uri.TryCreate(WebUtility.HtmlDecode(value), UriKind.Absolute, out uri!) &&
               uri.Scheme is "https" or "http" && uri.UserInfo.Length == 0;
    }

    public static string Render(CommunityPost post, bool dark)
    {
        var raw = post.Raw;
        var body = new StringBuilder();
        bool structured = TryRenderJson(body, raw.Get("post").Text("structured_content"), raw) ||
                          TryRenderJson(body, raw.Get("post").Text("content"), raw);
        if (!structured)
        {
            var html = raw.Get("post").Text("content");
            if (html.Length > 0) body.Append(Sanitize(html));
            else body.Append("<p>" + Encode(post.Summary) + "</p>");
            foreach (var image in raw.Get("image_list").Items())
                if (!body.ToString().Contains(Encode(image.Text("url")), StringComparison.Ordinal))
                    AddImage(body, image.Text("url"));
        }

        var attachments = new StringBuilder();
        foreach (var image in raw.Get("image_list").Items())
        {
            string url = image.Text("url");
            if (url.Length > 0 && !body.ToString().Contains(Encode(url), StringComparison.Ordinal))
                AddImage(attachments, url);
        }

        if (raw.Get("post").Number("view_type") == 2) body.Insert(0, attachments);
        else body.Append(attachments);
        foreach (var vod in raw.Get("vod_list").Items())
        {
            if (!body.ToString().Contains("data-vod=\"" + Encode(vod.Text("id")) + "\"", StringComparison.Ordinal))
                AddVideo(body, vod);
        }

        string bg = dark ? "#15171a" : "#ffffff", fg = dark ? "#e8ecef" : "#242424";
        return $$"""
                 <!doctype html><html><head><meta charset="utf-8">
                 <meta name="viewport" content="width=device-width,initial-scale=1">
                 <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src https: http:; media-src https: http:; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-src 'none'">
                 <meta name="referrer" content="no-referrer">
                 <style>
                 :root { color-scheme: {{(dark ? "dark" : "light")}}; } * { box-sizing:border-box; }
                 body { margin:0 auto; max-width:940px; padding:26px 28px 48px; background:{{bg}}; color:{{fg}}; font:16px/1.9 'Segoe UI','Microsoft YaHei UI',sans-serif; overflow-wrap:anywhere; }
                 p { margin:0 0 14px; } h1,h2,h3 { line-height:1.45; } a { color:{{(dark ? "#8dd6ff" : "#006db1")}}; text-decoration:none; } a:hover { text-decoration:underline; }
                 img,video { max-width:100%; height:auto; border-radius:8px; } img { display:block; margin:12px auto; } video { width:100%; margin:16px 0; }
                 a:has(img) { cursor:zoom-in; } a:has(img):hover { filter:brightness(1.06); }
                 blockquote { border-left:3px solid #6693ad; padding:8px 16px; margin:12px 0; background:#80808018; }
                 pre,code { white-space:pre-wrap; background:#80808018; border-radius:6px; } pre { padding:14px; }
                 table { border-collapse:collapse; max-width:100%; } td,th { border:1px solid #80808060; padding:6px 10px; }
                 .card { display:block; border:1px solid #80808040; border-radius:8px; padding:12px; margin:12px 0; } .muted { opacity:.65; } hr { border:0; border-top:1px solid #80808050; margin:20px 0; }
                 #community-comments:empty { display:none; } #community-comments { border-top:1px solid #80808040; margin-top:32px; padding-top:24px; }
                 .comments-header { display:flex; align-items:center; justify-content:space-between; gap:16px; margin-bottom:20px; }
                 .comments-heading { display:flex; align-items:center; gap:12px; flex-shrink:0; }
                 .comments-header h2 { margin:0; font-size:20px; white-space:nowrap; } .comments-header nav { display:flex; gap:8px; overflow-x:auto; min-width:0; margin-left:auto; }
                 .comment-pill { display:inline-block; padding:6px 14px; border-radius:20px; background:{{(dark ? "#282b30" : "#eff1f3")}}; color:{{fg}}; font-size:13px; white-space:nowrap; text-decoration:none; }
                 .comment-pill.selected { background:{{(dark ? "#e8ecef" : "#23272b")}}; color:{{(dark ? "#14191e" : "#ffffff")}}; }
                 .comment { border-bottom:1px solid #80808028; padding:16px 0; } .comment-author { display:flex; gap:12px; align-items:center; flex-wrap:wrap; font-size:13px; }
                 .comment-author .muted { font-size:11px; } .comment p { margin:10px 0; font-size:15px; line-height:1.75; }
                 .comment-user { display:inline-flex; align-items:center; gap:10px; }
                 a.comment-user:has(img) { cursor:pointer; } a.comment-user:hover { text-decoration:none; } a.comment-user:hover .comment-name { text-decoration:underline; }
                 .comment-avatar { display:inline-flex; align-items:center; justify-content:center; position:relative; width:32px; height:32px; flex-shrink:0; overflow:hidden; border-radius:50%; background:#80808028; color:{{fg}}; }
                 .comment-avatar svg { width:18px; height:18px; } .comment-avatar img { position:absolute; inset:0; display:block; width:100%; height:100%; margin:0; object-fit:cover; border-radius:50%; }
                 .child-comment .comment-avatar { width:24px; height:24px; } .child-comment .comment-avatar svg { width:14px; height:14px; }
                 .comment-actions { display:flex; align-items:center; gap:8px; margin:8px 0; } .comment-actions .comment-pill { font-size:12px; padding:4px 12px; }
                 .comment-like { display:inline-flex; align-items:center; gap:5px; margin-left:auto; } .comment-like svg { width:14px; height:14px; flex-shrink:0; }
                 .child-comment { margin-left:20px; padding-left:16px; border-left:2px solid #80808030; }
                 .comment-image { display:inline-block; width:auto; max-width:250px; max-height:160px; object-fit:contain; margin:8px 8px 8px 0; vertical-align:top; }
                 </style></head><body><article>{{body}}</article><section id="community-comments" aria-live="polite"></section></body></html>
                 """;
    }

    private static bool TryRenderJson(StringBuilder body, string content, JsonElement raw)
    {
        if (content.Length == 0) return false;
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            var ops = root.ValueKind == JsonValueKind.Array ? root : root.Get("ops");
            if (ops.ValueKind == JsonValueKind.Array) RenderStructured(body, ops, raw);
            else if (root.Text("describe").Length > 0 || root.Get("imgs").ValueKind == JsonValueKind.Array)
            {
                string caption = root.Text("describe");
                if (caption.Length > 0) body.Append("<p>" + Linkify(caption).Replace("\n", "<br>") + "</p>");
                foreach (var image in root.Get("imgs").Items())
                    AddImage(body, image.ValueKind == JsonValueKind.String ? image.GetString()! : image.Text("url"));
                return true;
            }
            else if (root.Get("text").ValueKind == JsonValueKind.Array)
            {
                RenderStructured(body, root.Get("text"), raw);
                foreach (var image in root.Get("images").Items()) AddImage(body, image.Text("image_url"));
                foreach (var vod in root.Get("vods").Items()) AddVideo(body, vod.Get("vod"));
            }

            return body.Length > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void RenderStructured(StringBuilder body, JsonElement ops, JsonElement full)
    {
        var line = new StringBuilder();

        void Flush(JsonElement attrs)
        {
            int heading = (int)attrs.Number("header");
            string tag = heading is >= 1 and <= 4 ? "h" + heading : attrs.Flag("blockquote") ? "blockquote" : "p";
            string align = attrs.Text("align");
            string style = align is "center" or "right" or "justify" ? " style=\"text-align:" + align + "\"" : "";
            if (attrs.Text("list") is "bullet" or "ordered")
            {
                string list = attrs.Text("list") == "ordered" ? "ol" : "ul";
                body.Append('<').Append(list).Append("><li>").Append(line).Append("</li></").Append(list).Append('>');
            }
            else
                body.Append('<').Append(tag).Append(style).Append('>')
                    .Append(line.Length == 0 ? "<br>" : line.ToString()).Append("</").Append(tag).Append('>');

            line.Clear();
        }

        foreach (var op in ops.EnumerateArray())
        {
            var insert = op.Get("insert");
            var attrs = op.Get("attributes");
            if (insert.ValueKind != JsonValueKind.String)
            {
                if (line.Length > 0) Flush(default);
                RenderInsert(body, op, full);
                continue;
            }

            string[] parts = (insert.GetString() ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0)
                {
                    using var segment = JsonDocument.Parse(JsonSerializer.Serialize(new
                    {
                        insert = parts[i],
                        attributes = attrs.ValueKind == JsonValueKind.Object ? attrs : (JsonElement?)null
                    }));
                    RenderInsert(line, segment.RootElement, full);
                }

                if (i < parts.Length - 1) Flush(attrs);
            }
        }

        if (line.Length > 0) Flush(default);
    }

    private static string Linkify(string text)
    {
        var result = new StringBuilder();
        int offset = 0;
        foreach (Match match in BareUrl.Matches(text))
        {
            string url = match.Value.TrimEnd('.', ',', ';', ')', '!', '?');
            result.Append(Encode(text[offset..match.Index]));
            result.Append("<a href=\"").Append(Encode(url)).Append("\">").Append(Encode(url)).Append("</a>");
            offset = match.Index + url.Length;
        }

        result.Append(Encode(text[offset..]));
        return result.ToString();
    }

    private static void RenderInsert(StringBuilder body, JsonElement op, JsonElement full)
    {
        var insert = op.Get("insert");
        var attrs = op.Get("attributes");
        if (insert.ValueKind == JsonValueKind.String)
        {
            string text = Linkify(insert.GetString() ?? "");
            if (TryWebUri(attrs.Text("link"), out var link))
                text = "<a href=\"" + Encode(link.AbsoluteUri) + "\">" + Encode(insert.GetString() ?? "") + "</a>";
            if (attrs.Flag("bold")) text = "<strong>" + text + "</strong>";
            if (attrs.Flag("italic")) text = "<em>" + text + "</em>";
            if (attrs.Flag("underline")) text = "<u>" + text + "</u>";
            if (attrs.Flag("strike")) text = "<s>" + text + "</s>";
            string style = "";
            string size = attrs.Text("size");
            if (size is "small" or "large" or "huge")
                style += $"font-size:{(size == "small" ? 13 : size == "large" ? 22 : 30)}px;";
            else if (Regex.IsMatch(size, @"^\d{1,2}(px|pt)$")) style += "font-size:" + size + ";";
            else if (int.TryParse(size, out int pixels)) style += "font-size:" + Math.Clamp(pixels, 10, 48) + "px;";
            if (style.Length > 0) text = "<span style=\"" + style + "\">" + text + "</span>";
            body.Append(text.Replace("\n", "<br>"));
            return;
        }

        if (insert.ValueKind != JsonValueKind.Object) return;
        if (insert.Text("image").Length > 0) AddImage(body, insert.Text("image"));
        else if (insert.Get("image").ValueKind == JsonValueKind.Object) AddImage(body, insert.Get("image").Text("url"));
        else if (insert.Get("custom_emoticon").ValueKind == JsonValueKind.Object)
            AddImage(body, insert.Get("custom_emoticon").Text("url"));
        else if (insert.Get("divider").ValueKind != JsonValueKind.Undefined) body.Append("<hr>");
        else if (insert.Get("vod").ValueKind != JsonValueKind.Undefined)
        {
            var vod = insert.Get("vod");
            string id = vod.ValueKind == JsonValueKind.String ? vod.GetString()! : vod.Text("id");
            var match = full.Get("vod_list").Items().FirstOrDefault(v => v.Text("id") == id);
            if (match.ValueKind == JsonValueKind.Object) AddVideo(body, match);
        }
        else if (TryWebUri(insert.Text("video"), out var video))
            body.Append("<video controls preload=\"none\" src=\"" + Encode(video.AbsoluteUri) + "\"></video>");
        else if (insert.Get("mention").ValueKind == JsonValueKind.Object)
        {
            var mention = insert.Get("mention");
            string uid = mention.Text("uid");
            if (uid.All(char.IsAsciiDigit) && uid.Length > 0)
                body.Append("<a href=\"" + ApiEndpoints.MiyousheAccountCenterUrl + Encode(uid) + "\">@" +
                            Encode(mention.Text("nickname")) + "</a>");
        }
        else if (insert.Get("link_card").ValueKind == JsonValueKind.Object)
        {
            var card = insert.Get("link_card");
            if (TryWebUri(card.Text("link") is { Length: > 0 } url ? url : card.Text("url"), out var uri))
                body.Append("<a class=\"card\" href=\"" + Encode(uri.AbsoluteUri) + "\">" + Encode(card.Text("title")) +
                            "</a>");
        }
        else
            body.Append("<span class=\"muted\">" + Encode(insert.Text("backup_text") is { Length: > 0 } backup
                ? backup
                : insert.Get("vote").ValueKind != JsonValueKind.Undefined
                    ? "[投票：请在原帖参与]"
                    : "[互动内容：请在原帖查看]") + "</span>");
    }

    private static void AddImage(StringBuilder body, string url)
    {
        if (TryWebUri(url, out var uri))
            body.Append("<a href=\"" + Encode(uri.AbsoluteUri) + "\"><img loading=\"lazy\" src=\"" +
                        Encode(uri.AbsoluteUri) + "\"></a>");
    }

    private static void AddVideo(StringBuilder body, JsonElement vod)
    {
        var resolution = vod.Get("resolutions").Items().OrderBy(v => v.Number("height")).LastOrDefault();
        if (!TryWebUri(resolution.Text("url"), out var uri)) return;
        body.Append("<video data-vod=\"" + Encode(vod.Text("id")) + "\" controls preload=\"none\" src=\"" +
                    Encode(uri.AbsoluteUri) + "\"");
        if (TryWebUri(vod.Text("cover"), out var cover)) body.Append(" poster=\"" + Encode(cover.AbsoluteUri) + "\"");
        body.Append("></video>");
    }

    public static string Sanitize(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var output = new StringBuilder();
        foreach (var node in doc.DocumentNode.ChildNodes) Clean(node, output);
        return output.ToString();
    }

    private static void Clean(HtmlNode node, StringBuilder output)
    {
        if (node.NodeType == HtmlNodeType.Text)
        {
            output.Append(Encode(HtmlEntity.DeEntitize(node.InnerText)));
            return;
        }

        if (node.NodeType != HtmlNodeType.Element) return;
        string name = node.Name.ToLowerInvariant();
        if (name is "script" or "style" or "iframe" or "object" or "embed" or "svg" or "form" or "input"
            or "button") return;
        bool allowed = Tags.Contains(name);
        if (allowed)
        {
            output.Append('<').Append(name);
            if (name is "a" or "img" &&
                TryWebUri(node.GetAttributeValue(name == "a" ? "href" : "src", ""), out var uri))
                output.Append(name == "a" ? " href=\"" : " src=\"").Append(Encode(uri.AbsoluteUri)).Append('"');
            string style = string.Join(";", node.GetAttributeValue("style", "").Split(';').Where(s =>
                Regex.IsMatch(s.Trim(),
                    @"^(font-size:\s*\d{1,2}(px|pt|em|%)|font-weight:\s*(bold|[1-9]00)|font-style:\s*italic|text-align:\s*(left|right|center|justify)|text-decoration:\s*(underline|line-through))$",
                    RegexOptions.IgnoreCase)));
            if (style.Length > 0) output.Append(" style=\"").Append(Encode(style)).Append('"');
            output.Append('>');
        }

        foreach (var child in node.ChildNodes) Clean(child, output);
        if (allowed && name is not ("img" or "br" or "hr")) output.Append("</").Append(name).Append('>');
    }

    public static bool TryInternalLink(Uri uri, out CommunityFeed kind, out string id)
    {
        kind = CommunityFeed.Forum;
        id = "";
        if (!ApiEndpoints.IsMiyousheCommunityHost(uri.Host)) return false;
        var post = Regex.Match(uri.AbsolutePath, @"/(?:article|detail)/(\d+)(?:/|$)");
        if (post.Success)
        {
            kind = CommunityFeed.Forum;
            id = post.Groups[1].Value;
            return true;
        }

        var query = uri.Query.TrimStart('?').Split('&').Select(s => s.Split('=', 2)).Where(s => s.Length == 2)
            .GroupBy(s => s[0], StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key,
                g => Uri.UnescapeDataString(g.Last()[1]), StringComparer.OrdinalIgnoreCase);
        kind = uri.AbsolutePath.Contains("accountCenter", StringComparison.OrdinalIgnoreCase) ? CommunityFeed.User
            : uri.AbsolutePath.Contains("topic", StringComparison.OrdinalIgnoreCase) ? CommunityFeed.Topic
            : uri.AbsolutePath.Contains("collection", StringComparison.OrdinalIgnoreCase) ? CommunityFeed.Collection
            : CommunityFeed.Forum;
        id = query.GetValueOrDefault(kind == CommunityFeed.User ? "id" :
            kind == CommunityFeed.Topic ? "topic_id" : "collection_id") ?? query.GetValueOrDefault("id") ?? "";
        if (kind == CommunityFeed.Forum) id = query.GetValueOrDefault("post_id") ?? "";
        return id.Length is > 0 and <= 20 && id.All(char.IsAsciiDigit);
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}