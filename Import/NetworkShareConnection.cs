using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace GlpiNg.Modules.Deployment.Import;

/// <summary>
/// Ouvre une session authentifiée vers un partage réseau Windows, le temps d'une lecture.
///
/// Un chemin UNC n'a pas de place pour des identifiants : <c>File.OpenRead</c> se présente avec le
/// compte du processus, et échoue en « accès refusé » si ce compte n'a pas de droits sur le
/// partage. La solution du système est d'établir d'abord une connexion vers le partage avec des
/// identifiants explicites (<c>WNetAddConnection2</c>) ; toute lecture ultérieure du même chemin,
/// par n'importe quelle API de fichier, l'emprunte alors.
///
/// Aucune lettre de lecteur n'est réservée : la connexion est sans nom local, donc invisible du
/// reste du système et sans risque de collision avec un lecteur déjà monté. Elle est refermée à la
/// libération.
///
/// Windows uniquement — <c>mpr.dll</c> n'a pas d'équivalent ailleurs. Sur un autre système, un
/// partage doit être monté par l'hôte avant le lancement, et <see cref="Connect"/> le dit au lieu
/// d'échouer obscurément.
/// </summary>
public sealed class NetworkShareConnection : IDisposable
{
    private const int ResourceTypeDisk = 1;
    private const int NoError = 0;
    private const int ErrorSessionCredentialConflict = 1219;

    private readonly string? _connectedShare;

    private NetworkShareConnection(string? connectedShare) => _connectedShare = connectedShare;

    /// <summary>Rien à ouvrir : chemin local, ou aucun identifiant fourni.</summary>
    public static NetworkShareConnection None { get; } = new(null);

    /// <summary>
    /// Établit la connexion vers le partage qui contient <paramref name="path"/>.
    /// </summary>
    /// <returns>La connexion, ou <c>null</c> avec <paramref name="failure"/> renseigné.</returns>
    public static NetworkShareConnection? Connect(string path, string? userName, string? password, out string? failure)
    {
        failure = null;

        if (string.IsNullOrWhiteSpace(userName))
        {
            return None;
        }

        if (ShareRoot(path) is not { } share)
        {
            // Un chemin local avec des identifiants : ils ne servent à rien, mais ce n'est pas une
            // erreur — la lecture se fera simplement avec le compte du processus.
            return None;
        }

        if (!OperatingSystem.IsWindows())
        {
            failure = "l'authentification d'un partage réseau n'est possible que sous Windows ; montez le partage sur l'hôte avant l'import";
            return null;
        }

        int result = AddConnection(share, userName, password);

        // Déjà connecté à ce partage sous une autre identité : ce n'est pas un échec de nos
        // identifiants, mais on ne peut pas en ouvrir une seconde. La session existante fera foi.
        if (result is NoError or ErrorSessionCredentialConflict)
        {
            return new NetworkShareConnection(result == NoError ? share : null);
        }

        failure = $"connexion à {share} refusée : {new Win32Exception(result).Message.TrimEnd('.', ' ')}";
        return null;
    }

    [SupportedOSPlatform("windows")]
    private static int AddConnection(string share, string userName, string? password)
    {
        NetResource resource = new()
        {
            Scope = 0,
            Type = ResourceTypeDisk,
            DisplayType = 0,
            Usage = 0,
            LocalName = null,
            RemoteName = share,
            Comment = null,
            Provider = null,
        };

        return WNetAddConnection2(ref resource, password, userName, 0);
    }

    /// <summary>
    /// Racine <c>\\serveur\partage</c> d'un chemin UNC, ou <c>null</c> si le chemin n'en est pas un.
    /// C'est le partage, et non le sous-dossier vis&#233;, qui porte la connexion.
    /// </summary>
    private static string? ShareRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string normalized = path.Replace('/', '\\');
        if (!normalized.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return null;
        }

        string[] segments = normalized[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2 ? $@"\\{segments[0]}\{segments[1]}" : null;
    }

    public void Dispose()
    {
        if (_connectedShare is null || !OperatingSystem.IsWindows())
        {
            return;
        }

        // Sans force : une lecture encore en cours ne doit pas être coupée sous ses pieds. L'échec
        // éventuel est sans conséquence — la session se refermera d'elle-même.
        WNetCancelConnection2(_connectedShare, 0, false);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NetResource
    {
        public int Scope;
        public int Type;
        public int DisplayType;
        public int Usage;
        [MarshalAs(UnmanagedType.LPWStr)] public string? LocalName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? RemoteName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Provider;
    }

    // DllImport et non LibraryImport : le générateur source de ce dernier ne prend pas en charge
    // le passage d'une structure par référence, et exige du code « unsafe » que ce module n'active
    // pas pour deux appels.
    [DllImport("mpr.dll", EntryPoint = "WNetAddConnection2W", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int WNetAddConnection2(ref NetResource netResource, string? password, string? userName, int flags);

    [DllImport("mpr.dll", EntryPoint = "WNetCancelConnection2W", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int WNetCancelConnection2(string name, int flags, [MarshalAs(UnmanagedType.Bool)] bool force);
}
