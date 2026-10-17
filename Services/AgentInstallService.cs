using System.Net;
using System.Text;
using GlpiNg.Modules.Abstractions.Storage;
using GlpiNg.Modules.Deployment.Models;

namespace GlpiNg.Modules.Deployment.Services;

/// <summary>Résultat de l'installation de l'agent sur un poste : voir <see cref="AgentInstallService.DeployAsync"/>.</summary>
public sealed record AgentInstallOutcome(string Host, bool Success, string Message)
{
    public static AgentInstallOutcome Failed(string host, string message) => new(host, false, message);
}

/// <summary>
/// Installe GLPI-Agent sur un poste distant qui n'a pas encore d'agent : impossible de passer
/// par le protocole /inventory, qui suppose justement un agent en place. Le serveur ouvre donc
/// un shell WinRM sur le poste (voir <see cref="WinRmClient"/>) et y lance une commande qui
/// <b>télécharge</b> l'installeur depuis ce serveur GlpiNg (voir
/// <c>AgentInstallerController</c>) puis l'exécute avec les options de
/// <see cref="AgentInstallSettings"/> — l'installeur ne transite jamais par le canal WinRM,
/// qui encode tout en base64 dans du SOAP et s'effondre sur plusieurs dizaines de mégaoctets.
///
/// ponytail: Windows/WinRM seulement. Les paquets Linux/macOS de l'agent s'installent par un
/// canal différent (SSH) : à ajouter le jour où c'est demandé, pas avant.
/// </summary>
public class AgentInstallService(IStoragePaths storagePaths)
{
    /// <summary>Nom du fichier écrit sur le poste distant, dans le dossier temporaire de l'administrateur.</summary>
    private const string RemoteFileName = "glpi-agent-installer";

    /// <summary>Dossier des installeurs téléversés, sous la racine de stockage (sauvegardée d'un bloc).</summary>
    public string InstallersPath => storagePaths.Ensure(Path.Combine(storagePaths.Root, "agent-installers"));

    public string InstallerFullPath(string fileName) => Path.Combine(InstallersPath, Path.GetFileName(fileName));

    /// <summary>
    /// Remplace l'installeur courant par <paramref name="content"/>. Les précédents sont
    /// supprimés : un seul installeur est servi à la fois, et un MSI oublié là reste
    /// téléchargeable par quiconque devine son nom.
    /// </summary>
    public async Task<long> SaveInstallerAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        string directory = InstallersPath;

        foreach (string existing in Directory.EnumerateFiles(directory))
        {
            File.Delete(existing);
        }

        string path = Path.Combine(directory, Path.GetFileName(fileName));

        await using (FileStream file = File.Create(path))
        {
            await content.CopyToAsync(file, cancellationToken);
        }

        return new FileInfo(path).Length;
    }

    /// <summary>
    /// Installe l'agent sur <paramref name="host"/>. <paramref name="installerUrl"/> est l'URL
    /// publique (jetonnée) depuis laquelle le poste télécharge l'installeur.
    /// </summary>
    public async Task<AgentInstallOutcome> DeployAsync(
        AgentInstallSettings settings,
        string host,
        Uri installerUrl,
        NetworkCredential credential,
        CancellationToken cancellationToken = default)
    {
        UriBuilder endpoint = new(settings.WinRmUseHttps ? "https" : "http", host, settings.WinRmPort, "wsman");

        try
        {
            WinRmResult result = await WinRmClient.RunAsync(
                endpoint.Uri,
                credential,
                settings.WinRmAcceptAnyCertificate,
                "powershell.exe",
                ["-NonInteractive", "-NoProfile", "-ExecutionPolicy", "Bypass", "-EncodedCommand", EncodeCommand(BuildScript(settings, installerUrl))],
                TimeSpan.FromMinutes(settings.TimeoutMinutes > 0 ? settings.TimeoutMinutes : 15),
                cancellationToken);

            string output = result.Output.Trim();

            // msiexec rend 3010 quand l'installation a réussi mais demande un redémarrage.
            return result.ExitCode is 0 or 3010
                ? new AgentInstallOutcome(host, true, output.Length > 0 ? output : "Installation terminée.")
                : AgentInstallOutcome.Failed(host, $"Code de sortie {result.ExitCode}. {output}".Trim());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AgentInstallOutcome.Failed(host, $"Délai dépassé ({settings.TimeoutMinutes} min).");
        }
        catch (Exception ex) when (ex is HttpRequestException or WinRmFaultException or InvalidOperationException or IOException)
        {
            return AgentInstallOutcome.Failed(host, ex.Message);
        }
    }

    /// <summary>
    /// Script PowerShell exécuté sur le poste : téléchargement du MSI puis installation
    /// silencieuse. Exposé pour que la page de configuration puisse le montrer tel quel — c'est
    /// aussi ce qu'un administrateur colle dans une GPO quand WinRM n'est pas ouvert.
    /// </summary>
    public static string BuildScript(AgentInstallSettings settings, Uri installerUrl)
    {
        string arguments = string.Join(", ",
            new[] { "'/i'", "$installer", "'/quiet'", "'/norestart'" }.Concat(MsiProperties(settings).Select(Quote)));

        StringBuilder script = new();
        script.AppendLine("$ErrorActionPreference = 'Stop'");
        script.AppendLine("[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12");
        script.AppendLine($"$installer = Join-Path $env:TEMP '{RemoteFileName}.msi'");
        script.AppendLine($"Invoke-WebRequest -UseBasicParsing -Uri '{Escape(installerUrl.ToString())}' -OutFile $installer");
        script.AppendLine($"$process = Start-Process -FilePath 'msiexec.exe' -ArgumentList {arguments} -Wait -PassThru");
        script.AppendLine("Remove-Item $installer -Force -ErrorAction SilentlyContinue");
        script.AppendLine("exit $process.ExitCode");

        return script.ToString();
    }

    /// <summary>Propriétés passées à l'installeur, au format <c>NOM=valeur</c> de l'installeur Windows de GLPI-Agent.</summary>
    private static IEnumerable<string> MsiProperties(AgentInstallSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ServerUrl))
        {
            yield return $"SERVER={settings.ServerUrl.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(settings.Tag))
        {
            yield return $"TAG={settings.Tag.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(settings.InstallDir))
        {
            yield return $"INSTALLDIR={settings.InstallDir.Trim()}";
        }

        if (settings.RunInventoryNow)
        {
            yield return "RUNNOW=1";
        }

        if (settings.NoSslCheck)
        {
            yield return "NO_SSL_CHECK=1";
        }

        if (settings.NoHttpd)
        {
            yield return "NO_HTTPD=1";
        }
        else
        {
            yield return $"HTTPD_PORT={settings.HttpdPort}";
        }

        foreach (AgentInstallProperty property in AgentInstallProperties.All)
        {
            string value = (settings.Properties.GetValueOrDefault(property.Name) ?? string.Empty).Trim();

            // Une valeur laissée à celle de l'installeur n'a pas à encombrer la ligne de commande.
            if (value.Length > 0 && !value.Equals(property.Default, StringComparison.Ordinal))
            {
                yield return $"{property.Name}={value}";
            }
        }
    }

    /// <summary>
    /// Le script part en <c>-EncodedCommand</c> : UTF-16LE base64, donc plus aucune question de
    /// guillemets, de retours à la ligne ou d'accents entre ici et le shell distant.
    /// </summary>
    public static string EncodeCommand(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    private static string Quote(string value) => $"'{Escape(value)}'";

    private static string Escape(string value) => value.Replace("'", "''");
}
