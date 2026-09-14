using System.Globalization;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;
using GlpiNg.Modules.Abstractions.Storage;

namespace GlpiNg.Modules.Deployment.Import;

/// <summary>Où aller chercher le contenu des fichiers de paquets d'une installation GLPI.</summary>
/// <param name="FilesPath">
/// Répertoire des fichiers du plugin d'inventaire, vu depuis la machine GlpiNg : chemin local ou
/// partage réseau. Route principale, parce qu'elle ne dépend d'aucune convention d'URL.
/// </param>
/// <param name="FilesUserName">
/// Compte à présenter au partage réseau qui héberge <paramref name="FilesPath"/>. Optionnel : sans
/// lui, la lecture se fait avec le compte du processus, ce qui suffit pour un chemin local ou un
/// partage déjà accessible.
/// </param>
/// <param name="FilesPassword">Mot de passe associé à <paramref name="FilesUserName"/>.</param>
/// <param name="BaseUrl">Racine HTTP de GLPI, pour la route authentifiée décrite ci-dessous.</param>
/// <param name="GlpiUserName">
/// Compte GLPI utilisé pour ouvrir une session web. Le seul point d'accès qui rend un fichier
/// entier (<c>front/deployfile_download.php</c>) vérifie le droit
/// <c>plugin_glpiinventory_package</c> : sans session, il n'y a rien à télécharger.
/// </param>
/// <param name="GlpiPassword">Mot de passe associé à <paramref name="GlpiUserName"/>.</param>
/// <param name="MirrorUrls">
/// Serveurs de miroir déclarés par le plugin. Un miroir est une copie statique du répertoire
/// <c>files/</c> servie par un serveur web ordinaire : ses manifestes et ses fragments s'y lisent
/// aux mêmes emplacements que sur le disque d'origine.
/// </param>
public sealed record GlpiDeployFileSource(
    string? FilesPath = null,
    string? FilesUserName = null,
    string? FilesPassword = null,
    string? BaseUrl = null,
    string? GlpiUserName = null,
    string? GlpiPassword = null,
    IReadOnlyList<string>? MirrorUrls = null)
{
    /// <summary>Manifeste d'un fichier sur un miroir : à plat, comme sur le disque de GLPI.</summary>
    public const string MirrorManifestUrlTemplate = "{base}/manifests/{sha512}";

    /// <summary>Fragment sur un miroir : sous son découpage d'empreinte, comme sur le disque.</summary>
    public const string MirrorPartUrlTemplate = "{base}/repository/{shard}/{sha512}";

    /// <summary>Point d'accès qui rend un fichier entier, déjà décompressé, en une requête.</summary>
    public const string DownloadUrlTemplate = "{base}/front/deployfile_download.php?deployfile_id={id}";

    public bool HasFilesPath => !string.IsNullOrWhiteSpace(FilesPath);

    /// <summary>Vrai quand la route authentifiée est utilisable : racine et compte renseignés.</summary>
    public bool HasGlpiSession => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(GlpiUserName);

    public IReadOnlyList<string> Mirrors =>
        [.. (MirrorUrls ?? []).Where(url => !string.IsNullOrWhiteSpace(url))];

    public bool Any => HasFilesPath || HasGlpiSession || Mirrors.Count > 0;
}

/// <summary>Ce qu'une tentative de récupération a produit.</summary>
/// <param name="TempFilePath">Fichier temporaire reconstitué, à supprimer par l'appelant. Null en cas d'échec.</param>
/// <param name="Failure">Raison de l'échec, formulée pour un administrateur. Null en cas de succès.</param>
public sealed record GlpiDeployFileFetchResult(string? TempFilePath, string? Failure)
{
    public bool Succeeded => TempFilePath is not null;
}

/// <summary>
/// Récupère le contenu d'un fichier de paquet depuis une installation GLPI, pour que l'import
/// n'ait pas à s'arrêter au descripteur.
///
/// Ce que le plugin fait de ses fichiers, d'après son code (<c>inc/deployfile.class.php</c>,
/// <c>inc/deployfilepart.class.php</c>, <c>public/b/deploy/index.php</c>) :
///
/// <list type="bullet">
/// <item>un fichier est découpé en fragments, chacun compressé en gzip et rangé dans
/// <c>files/repository/{premier caractère}/{deux premiers}/{empreinte du fragment}</c> ;</item>
/// <item>la liste de ses fragments vit dans un manifeste, <c>files/manifests/{empreinte du
/// fichier}</c>, à plat, une empreinte par ligne ;</item>
/// <item>cette liste n'est <b>nulle part en base</b> : la table <c>deployfiles</c> ne porte que le
/// nom, la taille, le type et l'empreinte.</item>
/// </list>
///
/// D'où les trois routes, et leurs limites respectives :
///
/// <list type="number">
/// <item><b>Disque</b> — manifeste puis fragments, lus aux emplacements ci-dessus. La seule qui
/// fonctionne sans rien d'autre.</item>
/// <item><b>Miroir</b> — un miroir est une copie statique de <c>files/</c> servie par un serveur
/// web ordinaire : mêmes chemins, en HTTP.</item>
/// <item><b>Session GLPI</b> — <c>front/deployfile_download.php?deployfile_id=</c> rend le fichier
/// entier déjà réassemblé et décompressé, mais vérifie le droit
/// <c>plugin_glpiinventory_package</c> : il faut donc ouvrir une session web.</item>
/// </list>
///
/// Le point d'accès des agents (<c>b/deploy/?action=getFilePart</c>) n'en est pas une : il ne sert
/// qu'un fragment à la fois, et l'agent n'apprend la liste des fragments que dans le JSON de son
/// job, construit à partir du manifeste. Sans manifeste, il n'y a rien à demander.
///
/// Le garde-fou tient dans l'empreinte : l'appelant réécrit le fichier reconstitué par le stockage
/// GlpiNg, qui en recalcule le SHA-512. S'il ne correspond pas à celui attendu, rien n'est
/// enregistré.
/// </summary>
public sealed partial class GlpiDeployFileFetcher(HttpClient httpClient) : IDisposable
{
    private Dictionary<string, string>? _repositoryIndex;
    private string? _indexedRoot;

    // Session vers le partage réseau, ouverte au premier accès et gardée pour toute la durée de
    // l'import : l'ouvrir et la refermer par fichier serait autant d'allers-retours d'authentification
    // pour rien.
    private NetworkShareConnection? _share;

    /// <summary>Résultat de la connexion à GLPI : tentée une fois. Chaîne vide en cas de succès,
    /// motif de l'échec sinon, <c>null</c> tant qu'elle n'a pas été tentée.</summary>
    private string? _glpiSessionFailure;

    // « deployFileId » : identifiant du fichier dans deployfiles, sans lequel la route authentifiée
    // n'a rien à demander — c'est le seul paramètre que deployfile_download.php accepte.
    public async Task<GlpiDeployFileFetchResult> FetchAsync(
        string sha512, GlpiDeployFileSource source, int? deployFileId = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha512);
        ArgumentNullException.ThrowIfNull(source);

        if (!source.Any)
        {
            return new GlpiDeployFileFetchResult(null, "aucune source de fichiers configurée");
        }

        List<string> attempts = [];

        if (source.HasFilesPath)
        {
            try
            {
                EnsureShareConnected(source);

                if (await FetchFromDiskAsync(sha512, source.FilesPath!, cancellationToken) is { } fromDisk)
                {
                    return new GlpiDeployFileFetchResult(fromDisk, null);
                }

                attempts.Add("introuvable dans le répertoire des fichiers");
            }
            catch (ShareConnectionException ex)
            {
                attempts.Add($"partage réseau : {ex.Message}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                attempts.Add($"répertoire des fichiers : {ex.Message}");
            }
        }

        foreach (string mirror in source.Mirrors)
        {
            try
            {
                if (await FetchFromMirrorAsync(sha512, mirror, cancellationToken) is { } fromMirror)
                {
                    return new GlpiDeployFileFetchResult(fromMirror, null);
                }

                attempts.Add($"rien sur le miroir {mirror}");
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                attempts.Add($"miroir {mirror} : {ex.Message}");
            }
        }

        if (source.HasGlpiSession)
        {
            if (deployFileId is not { } fileId)
            {
                attempts.Add("session GLPI : identifiant du fichier inconnu dans la table deployfiles");
            }
            else
            {
                try
                {
                    if (await FetchOverGlpiSessionAsync(fileId, source, cancellationToken) is { } fromGlpi)
                    {
                        return new GlpiDeployFileFetchResult(fromGlpi, null);
                    }

                    attempts.Add($"rien à {DownloadUrl(fileId, source)} (session ouverte ?)");
                }
                catch (GlpiSessionException ex)
                {
                    attempts.Add($"session GLPI : {ex.Message}");
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
                {
                    attempts.Add($"session GLPI : {ex.Message}");
                }
            }
        }

        return new GlpiDeployFileFetchResult(null, string.Join(" ; ", attempts));
    }

    // ---- Route disque ------------------------------------------------------

    /// <summary>
    /// Ouvre la session vers le partage si des identifiants ont été fournis, une seule fois par
    /// exécution. Sans identifiants, ou pour un chemin local, il n'y a rien à ouvrir.
    /// </summary>
    private void EnsureShareConnected(GlpiDeployFileSource source)
    {
        if (_share is not null)
        {
            return;
        }

        _share = NetworkShareConnection.Connect(source.FilesPath!, source.FilesUserName, source.FilesPassword, out string? failure)
            ?? throw new ShareConnectionException(failure ?? "connexion au partage impossible");
    }

    /// <summary>Échec d'ouverture du partage, distingué d'une erreur de lecture pour que le message
    /// rendu à l'administrateur désigne la bonne cause.</summary>
    private sealed class ShareConnectionException(string message) : Exception(message);

    private async Task<string?> FetchFromDiskAsync(string sha512, string root, CancellationToken cancellationToken)
    {
        // Un petit fichier peut être stocké entier, sans manifeste ni découpage.
        IReadOnlyList<string> partHashes = Locate(root, sha512, manifest: true) is { } manifestPath
            ? ParseManifest(await File.ReadAllLinesAsync(manifestPath, cancellationToken))
            : [sha512];

        List<string> partPaths = [];
        foreach (string part in partHashes)
        {
            if (Locate(root, part, manifest: false) is not { } partPath)
            {
                return null;
            }

            partPaths.Add(partPath);
        }

        return await AssembleAsync(
            partPaths.Select(path => (Func<CancellationToken, Task<Stream>>)(ct =>
                Task.FromResult<Stream>(File.OpenRead(path)))),
            cancellationToken);
    }

    /// <summary>
    /// Chemin d'un manifeste ou d'un fragment dans le dépôt.
    ///
    /// Les emplacements connus sont essayés en premier — GLPI range ses fichiers sous
    /// <c>{premier caractère}/{deux premiers}/{empreinte}</c> — parce qu'un accès direct coûte une
    /// requête au système de fichiers, là où parcourir l'arborescence d'un dépôt de plusieurs
    /// milliers de fichiers peut prendre des minutes sur un partage réseau.
    ///
    /// L'index complet reste en filet de sécurité, construit seulement si aucun de ces chemins ne
    /// répond : ce découpage est une convention interne au plugin, qui a déjà changé avec les
    /// versions, et chercher par nom fonctionne quelle qu'elle soit.
    /// </summary>
    private string? Locate(string root, string sha512, bool manifest)
    {
        string shard = Shard(sha512).Replace('/', Path.DirectorySeparatorChar);

        string[] known = manifest
            ?
            [
                Path.Combine(root, "manifests", sha512),
                Path.Combine(root, "manifests", shard, sha512),
            ]
            :
            [
                Path.Combine(root, "repository", shard, sha512),
                Path.Combine(root, shard, sha512),
                Path.Combine(root, "repository", sha512),
                Path.Combine(root, sha512),
            ];

        foreach (string candidate in known)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        Dictionary<string, string> index = BuildIndex(root);
        return index.GetValueOrDefault(manifest ? ManifestKey(sha512) : sha512);
    }

    /// <summary>
    /// Index nom de fichier → chemin, bâti une fois par répertoire source, et seulement quand les
    /// emplacements connus n'ont rien donné (voir <see cref="Locate"/>).
    /// </summary>
    private Dictionary<string, string> BuildIndex(string root)
    {
        if (_repositoryIndex is not null && string.Equals(_indexedRoot, root, StringComparison.OrdinalIgnoreCase))
        {
            return _repositoryIndex;
        }

        Dictionary<string, string> index = new(StringComparer.OrdinalIgnoreCase);

        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(path);

            // Un manifeste et le fichier entier qu'il décrit portent la même empreinte pour nom :
            // seul le répertoire les distingue. On garde donc les manifestes sous une clé à part.
            bool isManifest = Path.GetDirectoryName(path)?
                .Contains("manifest", StringComparison.OrdinalIgnoreCase) == true;

            index[isManifest ? ManifestKey(name) : name] = path;
        }

        _indexedRoot = root;
        return _repositoryIndex = index;
    }

    private static string ManifestKey(string sha512) => "manifest:" + sha512;

    // ---- Route miroir ------------------------------------------------------

    /// <summary>
    /// Reconstitue un fichier depuis un miroir, qui est une copie statique du répertoire
    /// <c>files/</c> : mêmes chemins que sur le disque de GLPI, servis en HTTP.
    /// </summary>
    private async Task<string?> FetchFromMirrorAsync(string sha512, string mirror, CancellationToken cancellationToken)
    {
        using HttpResponseMessage manifestResponse = await httpClient.GetAsync(
            Format(GlpiDeployFileSource.MirrorManifestUrlTemplate, mirror, sha512), cancellationToken);

        if (!manifestResponse.IsSuccessStatusCode)
        {
            return null;
        }

        string manifest = await manifestResponse.Content.ReadAsStringAsync(cancellationToken);
        List<string> partHashes = ParseManifest(manifest.Split('\n'));

        if (partHashes.Count == 0)
        {
            return null;
        }

        List<string> urls = [.. partHashes.Select(part => Format(GlpiDeployFileSource.MirrorPartUrlTemplate, mirror, part))];

        // Le premier fragment sert de sonde : inutile d'ouvrir un fichier temporaire pour découvrir
        // que le miroir ne sert pas le dépôt à cet endroit.
        using (HttpResponseMessage probe = await httpClient.GetAsync(
            urls[0], HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            if (!probe.IsSuccessStatusCode)
            {
                return null;
            }
        }

        return await AssembleAsync(urls.Select(url => Opener(url)), cancellationToken);
    }

    // ---- Route session GLPI ------------------------------------------------

    /// <summary>
    /// Télécharge le fichier entier via <c>front/deployfile_download.php</c>, qui le réassemble et
    /// le décompresse côté GLPI. Une requête par fichier, et rien à savoir du découpage.
    ///
    /// Ce point d'accès vérifie un droit : il faut donc y arriver avec une session web ouverte,
    /// d'où la connexion préalable au formulaire de GLPI.
    /// </summary>
    private async Task<string?> FetchOverGlpiSessionAsync(int deployFileId, GlpiDeployFileSource source, CancellationToken cancellationToken)
    {
        if (await EnsureGlpiSessionAsync(source, cancellationToken) is { } loginFailure)
        {
            throw new GlpiSessionException(loginFailure);
        }

        using HttpResponseMessage response = await httpClient.GetAsync(
            DownloadUrl(deployFileId, source), HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        // Une session refusée ne rend pas une erreur mais la page de connexion : le type de contenu
        // est le seul indice avant d'avoir tout téléchargé.
        if (!response.IsSuccessStatusCode
            || response.Content.Headers.ContentType?.MediaType is "text/html")
        {
            return null;
        }

        return await AssembleAsync(
            [ct => response.Content.ReadAsStreamAsync(ct)], cancellationToken);
    }

    /// <summary>Échec d'ouverture de session, porteur de sa cause exacte.</summary>
    private sealed class GlpiSessionException(string message) : Exception(message);

    /// <summary>
    /// Ouvre une session web sur GLPI, une seule fois par exécution.
    ///
    /// Le formulaire porte un jeton anti-rejeu (<c>_glpi_csrf_token</c>) qu'il faut lui reprendre :
    /// une soumission sans ce jeton est rejetée en 400, ce qui est le premier symptôme d'une
    /// extraction ratée. Le jeton est donc cherché dans les deux ordres d'attributs — GLPI rend
    /// tantôt <c>name</c> avant <c>value</c>, tantôt l'inverse selon la version — et son absence
    /// est signalée pour elle-même plut&#244;t que laissée devenir un 400 inexplicable.
    ///
    /// La requête se présente comme le ferait le formulaire : m&#234;me <c>Referer</c>, m&#234;me type de
    /// contenu. GLPI v&#233;rifie l'un et l'autre selon sa configuration.
    /// </summary>
    /// <returns><c>null</c> si la session est ouverte, sinon la raison de l'échec.</returns>
    private async Task<string?> EnsureGlpiSessionAsync(GlpiDeployFileSource source, CancellationToken cancellationToken)
    {
        if (_glpiSessionFailure is { } known)
        {
            return known.Length == 0 ? null : known;
        }

        string failure = await OpenGlpiSessionAsync(source, cancellationToken) ?? string.Empty;
        _glpiSessionFailure = failure;

        return failure.Length == 0 ? null : failure;
    }

    private async Task<string?> OpenGlpiSessionAsync(GlpiDeployFileSource source, CancellationToken cancellationToken)
    {
        string loginUrl = $"{source.BaseUrl!.TrimEnd('/')}/front/login.php";

        using HttpResponseMessage page = await httpClient.GetAsync(loginUrl, cancellationToken);
        if (!page.IsSuccessStatusCode)
        {
            return $"{loginUrl} a répondu {(int)page.StatusCode} {page.ReasonPhrase}";
        }

        string html = await page.Content.ReadAsStringAsync(cancellationToken);

        if (FindCsrfToken(html) is not { } token)
        {
            return $"jeton anti-rejeu introuvable sur {loginUrl} — page de connexion inattendue "
                + "(GLPI derrière un portail d'authentification, ou version non reconnue)";
        }

        FormUrlEncodedContent form = new(new Dictionary<string, string>
        {
            ["login_name"] = source.GlpiUserName!,
            ["login_password"] = source.GlpiPassword ?? string.Empty,
            ["_glpi_csrf_token"] = token,
            ["submit"] = "Valider",
        });

        using HttpRequestMessage request = new(HttpMethod.Post, loginUrl) { Content = form };
        request.Headers.Referrer = new Uri(loginUrl);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return $"connexion refusée : {(int)response.StatusCode} {response.ReasonPhrase}";
        }

        // GLPI rend de nouveau le formulaire quand l'authentification échoue, et l'accueil sinon :
        // la présence du champ de mot de passe est le signe le plus stable d'un échec.
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        return body.Contains("login_password", StringComparison.Ordinal)
            ? "identifiant ou mot de passe refusé par GLPI"
            : null;
    }

    /// <summary>Jeton anti-rejeu du formulaire, quel que soit l'ordre de ses attributs.</summary>
    private static string? FindCsrfToken(string html)
    {
        if (CsrfTokenRegex().Match(html) is { Success: true } nameFirst)
        {
            return nameFirst.Groups["token"].Value;
        }

        return CsrfTokenReversedRegex().Match(html) is { Success: true } valueFirst
            ? valueFirst.Groups["token"].Value
            : null;
    }

    private static string DownloadUrl(int deployFileId, GlpiDeployFileSource source) =>
        GlpiDeployFileSource.DownloadUrlTemplate
            .Replace("{base}", source.BaseUrl!.TrimEnd('/'), StringComparison.Ordinal)
            .Replace("{id}", deployFileId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private Func<CancellationToken, Task<Stream>> Opener(string url) => async ct =>
    {
        HttpResponseMessage response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(ct);
    };

    [GeneratedRegex("""name=["']_glpi_csrf_token["'][^>]*?value=["'](?<token>[^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex CsrfTokenRegex();

    [GeneratedRegex("""value=["'](?<token>[^"']+)["'][^>]*?name=["']_glpi_csrf_token["']""", RegexOptions.IgnoreCase)]
    private static partial Regex CsrfTokenReversedRegex();

    // ---- Commun ------------------------------------------------------------

    private static string Format(string template, string baseUrl, string sha512) => template
        .Replace("{base}", baseUrl.TrimEnd('/'), StringComparison.Ordinal)
        .Replace("{shard}", Shard(sha512), StringComparison.Ordinal)
        .Replace("{sha512}", sha512, StringComparison.Ordinal);

    /// <summary>
    /// Découpage en sous-dossiers d'une empreinte, tel que GLPI range son dépôt : premier
    /// caractère, puis les deux premiers. <c>000dfeead…</c> vit sous <c>0/00/</c>.
    ///
    /// Reprend <c>PluginGlpiinventoryDeployFile::getDirBySha512()</c> du plugin, où c'est ce qui
    /// évite d'entasser des milliers de fichiers dans un seul répertoire.
    /// </summary>
    public static string Shard(string sha512) =>
        sha512.Length >= 2 ? $"{sha512[0]}/{sha512[..2]}" : sha512;


    /// <summary>Empreintes hexadécimales du manifeste, une par ligne, commentaires et vides ignorés.</summary>
    private static List<string> ParseManifest(IEnumerable<string> lines) =>
    [
        .. lines
            .Select(line => line.Trim())
            .Where(line => line.Length >= 32 && line.All(Uri.IsHexDigit))
    ];

    /// <summary>
    /// Concatène les fragments dans un fichier temporaire, en décompressant ceux qui le sont.
    /// Passe par le disque et non par la mémoire : un paquet de plusieurs gigaoctets n'a aucune
    /// raison d'y tenir.
    /// </summary>
    private static async Task<string> AssembleAsync(
        IEnumerable<Func<CancellationToken, Task<Stream>>> parts, CancellationToken cancellationToken)
    {
        string tempPath = Path.GetTempFileName();

        try
        {
            await using FileStream output = File.Create(tempPath);

            foreach (Func<CancellationToken, Task<Stream>> open in parts)
            {
                await using Stream part = await open(cancellationToken);
                await using Stream buffered = await DecompressIfNeededAsync(part, cancellationToken);
                await buffered.CopyToAsync(output, cancellationToken);
            }

            return tempPath;
        }
        catch
        {
            File.Delete(tempPath);
            throw;
        }
    }

    /// <summary>
    /// Enveloppe le flux d'un fragment dans un décompresseur gzip s'il en porte l'en-tête.
    ///
    /// Détecté sur les deux premiers octets plutôt que sur une extension ou un réglage : le plugin
    /// compresse selon sa configuration, et un fragment stocké tel quel doit passer sans encombre.
    /// </summary>
    private static async Task<Stream> DecompressIfNeededAsync(Stream part, CancellationToken cancellationToken)
    {
        byte[] header = new byte[2];
        int read = await part.ReadAtLeastAsync(header, 2, throwOnEndOfStream: false, cancellationToken);

        Stream rewound = new ConcatenatedStream(new MemoryStream(header, 0, read), part);

        return read == 2 && header[0] == 0x1F && header[1] == 0x8B
            ? new GZipStream(rewound, CompressionMode.Decompress)
            : rewound;
    }

    public void Dispose() => _share?.Dispose();

    /// <summary>
    /// Rend lisibles bout à bout deux flux, pour rendre au fragment les octets consommés en
    /// cherchant son en-tête — un flux réseau ne se rembobine pas.
    /// </summary>
    private sealed class ConcatenatedStream(Stream first, Stream second) : Stream
    {
        private bool _firstDone;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!_firstDone)
            {
                int read = first.Read(buffer, offset, count);
                if (read > 0)
                {
                    return read;
                }

                _firstDone = true;
            }

            return second.Read(buffer, offset, count);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_firstDone)
            {
                int read = await first.ReadAsync(buffer, cancellationToken);
                if (read > 0)
                {
                    return read;
                }

                _firstDone = true;
            }

            return await second.ReadAsync(buffer, cancellationToken);
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                first.Dispose();
                second.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
