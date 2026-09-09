using System.IO.Compression;
using System.Net.Http;

namespace GlpiNg.Modules.Deployment.Import;

/// <summary>Où aller chercher le contenu des fichiers de paquets d'une installation GLPI.</summary>
/// <param name="FilesPath">
/// Répertoire des fichiers du plugin d'inventaire, vu depuis la machine GlpiNg : chemin local ou
/// partage réseau. Route principale, parce qu'elle ne dépend d'aucune convention d'URL.
/// </param>
/// <param name="BaseUrl">Racine HTTP de GLPI, utilisée en repli quand le disque n'est pas joignable.</param>
/// <param name="ManifestUrlTemplate">
/// Gabarit d'URL du manifeste d'un fichier, où <c>{base}</c> est <paramref name="BaseUrl"/> et
/// <c>{sha512}</c> l'empreinte du fichier entier.
/// </param>
/// <param name="PartUrlTemplate">Gabarit d'URL d'un fragment, mêmes marqueurs.</param>
public sealed record GlpiDeployFileSource(
    string? FilesPath = null,
    string? BaseUrl = null,
    string? ManifestUrlTemplate = null,
    string? PartUrlTemplate = null)
{
    /// <summary>
    /// Tracés par défaut, calqués sur le dépôt du plugin GLPI Inventory. Réglables plutôt que figés
    /// : ils ont changé entre FusionInventory et GLPI Inventory, et rien ici ne permet de vérifier
    /// lequel sert en face. Quand une récupération échoue, l'URL réellement demandée est rapportée
    /// telle quelle — c'est ce qui permet de corriger le gabarit sans toucher au code.
    /// </summary>
    public const string DefaultManifestUrlTemplate = "{base}/plugins/glpiinventory/b/deploy/manifests/{sha512}";

    /// <inheritdoc cref="DefaultManifestUrlTemplate"/>
    public const string DefaultPartUrlTemplate = "{base}/plugins/glpiinventory/b/deploy/repository/{sha512}";

    public bool HasFilesPath => !string.IsNullOrWhiteSpace(FilesPath);
    public bool HasBaseUrl => !string.IsNullOrWhiteSpace(BaseUrl);
    public bool Any => HasFilesPath || HasBaseUrl;
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
/// GLPI range ces fichiers déjà découpés en fragments et compressés, avec un manifeste par fichier
/// qui liste les empreintes de ses fragments. La reconstitution consiste donc à lire le manifeste,
/// récupérer chaque fragment, le décompresser et concaténer.
///
/// Rien n'est supposé de la disposition des répertoires : les fragments sont retrouvés par leur nom
/// au moyen d'un index bâti une fois pour toutes sur l'arborescence, quel que soit son découpage en
/// sous-dossiers. De même, la compression est détectée sur le contenu — les deux octets d'en-tête
/// gzip — plutôt que déduite d'une extension. C'est ce qui rend cette route utilisable sans avoir
/// pu la confronter à une installation réelle.
///
/// Le garde-fou tient dans l'empreinte : l'appelant réécrit le fichier reconstitué par le stockage
/// GlpiNg, qui en recalcule le SHA-512. S'il ne correspond pas à celui attendu, c'est qu'une
/// hypothèse était fausse — et rien n'est enregistré.
/// </summary>
public sealed class GlpiDeployFileFetcher(HttpClient httpClient)
{
    private Dictionary<string, string>? _repositoryIndex;
    private string? _indexedRoot;

    public async Task<GlpiDeployFileFetchResult> FetchAsync(
        string sha512, GlpiDeployFileSource source, CancellationToken cancellationToken = default)
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
                if (await FetchFromDiskAsync(sha512, source.FilesPath!, cancellationToken) is { } fromDisk)
                {
                    return new GlpiDeployFileFetchResult(fromDisk, null);
                }

                attempts.Add("introuvable dans le répertoire des fichiers");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                attempts.Add($"répertoire des fichiers : {ex.Message}");
            }
        }

        if (source.HasBaseUrl)
        {
            try
            {
                if (await FetchOverHttpAsync(sha512, source, cancellationToken) is { } fromHttp)
                {
                    return new GlpiDeployFileFetchResult(fromHttp, null);
                }

                attempts.Add($"HTTP : rien à {ManifestUrl(sha512, source)}");
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                attempts.Add($"HTTP : {ex.Message}");
            }
        }

        return new GlpiDeployFileFetchResult(null, string.Join(" ; ", attempts));
    }

    // ---- Route disque ------------------------------------------------------

    private async Task<string?> FetchFromDiskAsync(string sha512, string root, CancellationToken cancellationToken)
    {
        Dictionary<string, string> index = BuildIndex(root);

        // Un petit fichier peut être stocké entier, sans manifeste ni découpage.
        IReadOnlyList<string> partHashes = index.TryGetValue(ManifestKey(sha512), out string? manifestPath)
            ? ParseManifest(await File.ReadAllLinesAsync(manifestPath, cancellationToken))
            : [sha512];

        List<string> partPaths = [];
        foreach (string part in partHashes)
        {
            if (!index.TryGetValue(part, out string? partPath))
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
    /// Index nom de fichier → chemin, bâti une fois par répertoire source.
    ///
    /// Parcourir toute l'arborescence plutôt que reconstituer le chemin d'un fragment depuis son
    /// empreinte : le découpage en sous-dossiers du dépôt est une convention interne au plugin,
    /// qui a changé avec les versions. Chercher par nom fonctionne quelle qu'elle soit.
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

    // ---- Route HTTP --------------------------------------------------------

    private async Task<string?> FetchOverHttpAsync(string sha512, GlpiDeployFileSource source, CancellationToken cancellationToken)
    {
        using HttpResponseMessage manifestResponse = await httpClient.GetAsync(
            ManifestUrl(sha512, source), cancellationToken);

        IReadOnlyList<string> partHashes;
        if (manifestResponse.IsSuccessStatusCode)
        {
            string manifest = await manifestResponse.Content.ReadAsStringAsync(cancellationToken);
            partHashes = ParseManifest(manifest.Split('\n'));
        }
        else
        {
            // Pas de manifeste : le fichier est peut-être servi entier sous sa propre empreinte.
            partHashes = [sha512];
        }

        if (partHashes.Count == 0)
        {
            return null;
        }

        List<string> urls = [.. partHashes.Select(part => PartUrl(part, source))];

        // Une première requête sert de sonde : inutile d'écrire un fichier temporaire pour
        // découvrir au premier fragment que le gabarit d'URL ne mène nulle part.
        using (HttpResponseMessage probe = await httpClient.GetAsync(
            urls[0], HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            if (!probe.IsSuccessStatusCode)
            {
                return null;
            }
        }

        return await AssembleAsync(
            urls.Select(url => (Func<CancellationToken, Task<Stream>>)(async ct =>
            {
                HttpResponseMessage response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStreamAsync(ct);
            })),
            cancellationToken);
    }

    private static string ManifestUrl(string sha512, GlpiDeployFileSource source) => Format(
        source.ManifestUrlTemplate ?? GlpiDeployFileSource.DefaultManifestUrlTemplate, source.BaseUrl!, sha512);

    private static string PartUrl(string sha512, GlpiDeployFileSource source) => Format(
        source.PartUrlTemplate ?? GlpiDeployFileSource.DefaultPartUrlTemplate, source.BaseUrl!, sha512);

    private static string Format(string template, string baseUrl, string sha512) => template
        .Replace("{base}", baseUrl.TrimEnd('/'), StringComparison.Ordinal)
        .Replace("{sha512}", sha512, StringComparison.Ordinal);

    // ---- Commun ------------------------------------------------------------

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
