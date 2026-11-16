using System.Text;
using System.Text.Json;
using FufuLauncher.Models.Miyoushe;

namespace FufuLauncher.Services.Miyoushe;

internal sealed record MiyousheAigisChallenge(string SessionId, JsonElement Parameters)
{
    public static MiyousheAigisChallenge? Parse(string raw)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            string session = document.RootElement.Text("session_id");
            var parameters = document.RootElement.Get("data");
            if (parameters.ValueKind == JsonValueKind.String)
            {
                using var nested = JsonDocument.Parse(parameters.GetString()!);
                parameters = nested.RootElement.Clone();
            }

            if (session.Length == 0 || parameters.Text("gt").Length == 0) return null;
            return new(session, parameters.Clone());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string CreateProof(JsonElement result) =>
        SessionId + ";" + Convert.ToBase64String(Encoding.UTF8.GetBytes(result.GetRawText()));
}
