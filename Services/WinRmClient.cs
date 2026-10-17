using System.Net;
using System.Text;
using System.Xml.Linq;

namespace GlpiNg.Modules.Deployment.Services;

/// <summary>Résultat d'une commande distante : code de sortie du processus et sortie console agrégée.</summary>
public sealed record WinRmResult(int ExitCode, string Output);

/// <summary>
/// Client WS-Management (WinRM) minimal : ouvre un shell sur un poste Windows distant, y lance
/// une commande, lit stdout/stderr jusqu'à la fin et referme le shell. Utilisé par
/// <see cref="AgentInstallService"/> pour installer GLPI-Agent sur un poste qui, par définition,
/// n'a pas encore d'agent — aucun autre canal n'existe à ce moment-là.
///
/// L'authentification est déléguée à <see cref="HttpClientHandler.Credentials"/> : .NET gère
/// Negotiate/NTLM de lui-même, y compris quand le serveur GlpiNg tourne sous Linux.
///
/// ponytail: client volontairement minimal — pas de stdin, pas de Kerberos ni CredSSP, pas de
/// reprise. Passer à une vraie bibliothèque WS-Man le jour où il faut l'authentification
/// déléguée (double saut) ou un transfert de fichier par le canal WinRM lui-même.
/// </summary>
public static class WinRmClient
{
    private const string Soap = "http://www.w3.org/2003/05/soap-envelope";
    private const string Addressing = "http://schemas.xmlsoap.org/ws/2004/08/addressing";
    private const string Wsman = "http://schemas.dmtf.org/wbem/wsman/1/wsman.xsd";
    private const string Shell = "http://schemas.microsoft.com/wbem/wsman/1/windows/shell";
    private const string CmdResource = "http://schemas.microsoft.com/wbem/wsman/1/windows/shell/cmd";
    private const string DoneState = "http://schemas.microsoft.com/wbem/wsman/1/windows/shell/CommandState/Done";

    /// <summary>
    /// Exécute <paramref name="command"/> et ses <paramref name="arguments"/> (passés tels quels,
    /// sans réinterprétation par cmd) sur <paramref name="endpoint"/>.
    /// </summary>
    public static async Task<WinRmResult> RunAsync(
        Uri endpoint,
        NetworkCredential credential,
        bool acceptAnyCertificate,
        string command,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using HttpClientHandler handler = new() { Credentials = credential, PreAuthenticate = false };

        if (acceptAnyCertificate)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        using HttpClient http = new(handler) { Timeout = TimeSpan.FromMinutes(2) };
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        CancellationToken ct = deadline.Token;

        string shellId = await CreateShellAsync(http, endpoint, ct);

        try
        {
            string commandId = await StartCommandAsync(http, endpoint, shellId, command, arguments, ct);
            return await ReceiveAsync(http, endpoint, shellId, commandId, ct);
        }
        finally
        {
            // Un shell laissé ouvert compte dans le quota MaxShellsPerUser de la machine cible
            // (2 par défaut) : sans cette fermeture, deux déploiements suffisent à la bloquer.
            try
            {
                await DeleteShellAsync(http, endpoint, shellId, CancellationToken.None);
            }
            catch (Exception)
            {
                // Rien de plus à faire : le shell finira par expirer de lui-même (IdleTimeout).
            }
        }
    }

    private static async Task<string> CreateShellAsync(HttpClient http, Uri endpoint, CancellationToken ct)
    {
        XElement body = new(XName.Get("Shell", Shell),
            new XElement(XName.Get("InputStreams", Shell), "stdin"),
            new XElement(XName.Get("OutputStreams", Shell), "stdout stderr"));

        XElement options = OptionSet(("WINRS_NOPROFILE", "FALSE"), ("WINRS_CODEPAGE", "65001"));

        XDocument response = await SendAsync(http, endpoint,
            "http://schemas.xmlsoap.org/ws/2004/09/transfer/Create", null, options, body, ct);

        return response.Descendants(XName.Get("ShellId", Shell)).FirstOrDefault()?.Value
            ?? throw new InvalidOperationException("WinRM : réponse sans ShellId.");
    }

    private static async Task<string> StartCommandAsync(
        HttpClient http, Uri endpoint, string shellId, string command, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        XElement body = new(XName.Get("CommandLine", Shell),
            new XElement(XName.Get("Command", Shell), command),
            arguments.Select(argument => new XElement(XName.Get("Arguments", Shell), argument)));

        // WINRS_SKIP_CMD_SHELL : la commande est lancée directement, sans passer par cmd.exe,
        // donc sans réinterprétation des guillemets et caractères spéciaux des arguments.
        XElement options = OptionSet(("WINRS_SKIP_CMD_SHELL", "TRUE"), ("WINRS_CONSOLEMODE_STDIN", "TRUE"));

        XDocument response = await SendAsync(http, endpoint,
            "http://schemas.microsoft.com/wbem/wsman/1/windows/shell/Command", shellId, options, body, ct);

        return response.Descendants(XName.Get("CommandId", Shell)).FirstOrDefault()?.Value
            ?? throw new InvalidOperationException("WinRM : réponse sans CommandId.");
    }

    private static async Task<WinRmResult> ReceiveAsync(
        HttpClient http, Uri endpoint, string shellId, string commandId, CancellationToken ct)
    {
        StringBuilder output = new();

        XElement body = new(XName.Get("Receive", Shell),
            new XElement(XName.Get("DesiredStream", Shell), new XAttribute("CommandId", commandId), "stdout stderr"));

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            XDocument response;
            try
            {
                response = await SendAsync(http, endpoint,
                    "http://schemas.microsoft.com/wbem/wsman/1/windows/shell/Receive", shellId, null, body, ct);
            }
            catch (WinRmFaultException ex) when (ex.IsOperationTimeout)
            {
                // Rien n'est arrivé pendant l'OperationTimeout annoncé dans l'en-tête : c'est le
                // fonctionnement normal d'une commande longue, on redemande.
                continue;
            }

            foreach (XElement stream in response.Descendants(XName.Get("Stream", Shell)))
            {
                if (!string.IsNullOrEmpty(stream.Value))
                {
                    output.Append(Encoding.UTF8.GetString(Convert.FromBase64String(stream.Value)));
                }
            }

            XElement? state = response.Descendants(XName.Get("CommandState", Shell)).FirstOrDefault();

            if (state?.Attribute("State")?.Value == DoneState)
            {
                string? exitCode = state.Element(XName.Get("ExitCode", Shell))?.Value;
                return new WinRmResult(int.TryParse(exitCode, out int code) ? code : -1, output.ToString());
            }
        }
    }

    private static Task DeleteShellAsync(HttpClient http, Uri endpoint, string shellId, CancellationToken ct)
        => SendAsync(http, endpoint, "http://schemas.xmlsoap.org/ws/2004/09/transfer/Delete", shellId, null, null, ct);

    private static XElement OptionSet(params (string Name, string Value)[] options)
        => new(XName.Get("OptionSet", Wsman),
            options.Select(option => new XElement(XName.Get("Option", Wsman), new XAttribute("Name", option.Name), option.Value)));

    private static async Task<XDocument> SendAsync(
        HttpClient http, Uri endpoint, string action, string? shellId, XElement? options, XElement? body, CancellationToken ct)
    {
        XNamespace s = Soap;
        XNamespace a = Addressing;
        XNamespace w = Wsman;

        XElement envelope = new(s + "Envelope",
            new XAttribute(XNamespace.Xmlns + "s", Soap),
            new XAttribute(XNamespace.Xmlns + "a", Addressing),
            new XAttribute(XNamespace.Xmlns + "w", Wsman),
            new XAttribute(XNamespace.Xmlns + "rsp", Shell),
            new XElement(s + "Header",
                new XElement(a + "To", endpoint.ToString()),
                new XElement(a + "ReplyTo",
                    new XElement(a + "Address", new XAttribute(s + "mustUnderstand", "true"),
                        "http://schemas.xmlsoap.org/ws/2004/08/addressing/role/anonymous")),
                new XElement(a + "Action", new XAttribute(s + "mustUnderstand", "true"), action),
                new XElement(a + "MessageID", $"uuid:{Guid.NewGuid():D}"),
                new XElement(w + "ResourceURI", new XAttribute(s + "mustUnderstand", "true"), CmdResource),
                new XElement(w + "MaxEnvelopeSize", new XAttribute(s + "mustUnderstand", "true"), "512000"),
                // Borne la durée d'une requête Receive sans borner la commande elle-même : au
                // delà, le serveur répond une faute TimedOut et la boucle redemande.
                new XElement(w + "OperationTimeout", "PT60S"),
                new XElement(w + "Locale", new XAttribute(XNamespace.Xml + "lang", "en-US"),
                    new XAttribute(s + "mustUnderstand", "false")),
                shellId is null
                    ? null
                    : new XElement(w + "SelectorSet",
                        new XElement(w + "Selector", new XAttribute("Name", "ShellId"), shellId)),
                options),
            new XElement(s + "Body", body));

        using StringContent content = new(envelope.ToString(SaveOptions.DisableFormatting), Encoding.UTF8);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/soap+xml") { CharSet = "UTF-8" };

        using HttpResponseMessage response = await http.PostAsync(endpoint, content, ct);
        string payload = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            // Une faute WS-Man arrive en HTTP 500 avec un corps SOAP exploitable ; tout le reste
            // (401, 404, proxy...) n'a que son code pour se décrire.
            throw WinRmFaultException.From(response.StatusCode, payload);
        }

        return XDocument.Parse(payload);
    }
}

/// <summary>Faute WS-Man, ou erreur HTTP renvoyée par le service WinRM de la machine cible.</summary>
public sealed class WinRmFaultException(string message, bool isOperationTimeout) : Exception(message)
{
    private const string Soap = "http://www.w3.org/2003/05/soap-envelope";

    /// <summary>Vrai pour <c>wsman:TimedOut</c> : la requête Receive n'avait rien à renvoyer, ce n'est pas une erreur.</summary>
    public bool IsOperationTimeout { get; } = isOperationTimeout;

    public static WinRmFaultException From(HttpStatusCode status, string payload)
    {
        string? reason = null;
        bool timedOut = false;

        try
        {
            XDocument document = XDocument.Parse(payload);
            reason = document.Descendants(XName.Get("Text", Soap)).FirstOrDefault()?.Value.Trim();
            timedOut = document.Descendants(XName.Get("Subcode", Soap))
                .Concat(document.Descendants(XName.Get("Code", Soap)))
                .Any(element => element.Value.Contains("TimedOut", StringComparison.OrdinalIgnoreCase));
        }
        catch (System.Xml.XmlException)
        {
            // Corps non XML (page d'erreur d'un proxy, par exemple) : seul le code HTTP parle.
        }

        return new WinRmFaultException(
            reason is { Length: > 0 }
                ? $"WinRM ({(int)status}) : {reason}"
                : $"WinRM : réponse HTTP {(int)status} {status}.",
            timedOut);
    }
}
