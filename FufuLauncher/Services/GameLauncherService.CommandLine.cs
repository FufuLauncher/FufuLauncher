using System.Text;

namespace FufuLauncher.Services;

public partial class GameLauncherService
{
    private static string BuildElevatedArgumentString(string gameExePath, string commandLineArgs)
    {
        var arguments = $"--elevated-inject {QuoteArgument(gameExePath)} --";
        return string.IsNullOrWhiteSpace(commandLineArgs) ? arguments : $"{arguments} {commandLineArgs}";
    }

    internal static string QuoteArgument(string argument, bool forceQuotes = false)
    {
        if (string.IsNullOrEmpty(argument)) return "\"\"";
        if (!forceQuotes &&
            !argument.Contains(' ') && !argument.Contains('\t') && !argument.Contains('\n') &&
            !argument.Contains('\v') && !argument.Contains('\"'))
        {
            return argument;
        }

        var sb = new StringBuilder();
        sb.Append('"');

        for (int i = 0; i < argument.Length; i++)
        {
            int backslashes = 0;
            while (i < argument.Length && argument[i] == '\\')
            {
                backslashes++;
                i++;
            }

            if (i == argument.Length)
            {
                sb.Append('\\', backslashes * 2);
                break;
            }
            else if (argument[i] == '"')
            {
                sb.Append('\\', backslashes * 2 + 1);
                sb.Append('"');
            }
            else
            {
                sb.Append('\\', backslashes);
                sb.Append(argument[i]);
            }
        }

        sb.Append('"');
        return sb.ToString();
    }
}
