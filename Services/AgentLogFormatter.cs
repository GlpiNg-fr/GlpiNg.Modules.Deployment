using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace GlpiNg.Modules.Deployment.Services;

/// <summary>
/// Rendu HTML du journal brut d'un job (DeploymentJob.Log / NetworkTaskJob.Log), ligne par ligne :
/// "[HH:mm:ss] [phase] message". Extrait en classe statique partagée depuis TaskDetail.razor.cs
/// (seul consommateur jusqu'ici) pour être réutilisé par NetworkTaskDetail.razor.cs plutôt que
/// dupliqué — même format de journal des deux côtés (voir AgentController.HandleSetStatusCoreAsync
/// et FormatStatusLogLine, communs aux deux types de job).
/// </summary>
public static class AgentLogFormatter
{
    private static readonly Regex LogLinePrefixRegex = new(
        @"^\[(?<time>\d{2}:\d{2}:\d{2})\]\s*(?:\[(?<tag>[a-zA-Z]+)\]\s*)?(?<rest>.*)$",
        RegexOptions.Compiled);

    public static MarkupString RenderLog(string log)
    {
        StringBuilder html = new();

        foreach (string rawLine in log.Replace("\r\n", "\n").Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            string trimmed = line.Trim();

            string lineClass = trimmed.Length > 0 && trimmed.All(c => c == '=')
                ? "glping-log-line glping-log-sep"
                : Regex.IsMatch(line, @"\(ok\)\s*$", RegexOptions.IgnoreCase) || line.Contains("success", StringComparison.OrdinalIgnoreCase)
                    ? "glping-log-line glping-log-ok"
                    : Regex.IsMatch(line, @"\(ko\)\s*$", RegexOptions.IgnoreCase)
                      || line.Contains("error", StringComparison.OrdinalIgnoreCase)
                      || line.Contains("failed", StringComparison.OrdinalIgnoreCase)
                        ? "glping-log-line glping-log-error"
                        : "glping-log-line";

            html.Append("<div class=\"").Append(lineClass).Append("\">");

            Match match = LogLinePrefixRegex.Match(line);
            if (match.Success)
            {
                html.Append("<span class=\"glping-log-time\">[").Append(WebUtility.HtmlEncode(match.Groups["time"].Value)).Append("]</span> ");
                if (match.Groups["tag"].Success)
                {
                    html.Append("<span class=\"glping-log-tag\">[").Append(WebUtility.HtmlEncode(match.Groups["tag"].Value)).Append("]</span> ");
                }

                html.Append(WebUtility.HtmlEncode(match.Groups["rest"].Value));
            }
            else
            {
                html.Append(WebUtility.HtmlEncode(line));
            }

            html.Append("</div>");
        }

        return new MarkupString(html.ToString());
    }
}
