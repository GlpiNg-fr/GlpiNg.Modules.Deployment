using System.Security.Cryptography;
using GlpiNg.Modules.Deployment.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace GlpiNg.Modules.Deployment.Services;

/// <summary>
/// Écrit sur disque (sous <c>PackageStorage:RootPath</c>) les fichiers uploadés depuis
/// l'admin des paquets de déploiement, et calcule les hashs SHA512 utilisés comme clefs par
/// <c>AgentController</c> pour servir <c>GET inventory/deploy/file/{sha512}</c> (fichier
/// entier, reconstitué à la volée) et <c>GET inventory/deploy/file/part/{sha512}</c> (un
/// fragment).
///
/// Chaque fichier est découpé en fragments de taille fixe (<c>PackageStorage:PartSizeBytes</c>,
/// 5 Mo par défaut) dès l'écriture — jamais stocké comme un blob unique. Reprend le principe du
/// découpage "multiparts" du protocole GLPI-Agent réel (voir <see cref="Models.DeploymentPackageFilePart"/>) :
/// téléchargement/vérification fragment par fragment côté agent (plus léger, reprise possible sur
/// échec réseau) et pas de doublon d'espace disque ici, puisqu'aucune copie "fichier entier"
/// n'est conservée à côté des fragments.
///
/// Consommé par <c>DeploymentPackageFilesController</c>, qui lit le fichier directement depuis
/// le corps multipart de la requête HTTP (<c>MultipartReader</c>) plutôt que via le composant
/// Blazor <c>InputFile</c> : ce dernier fait transiter chaque octet par le circuit SignalR, dont
/// la limite de taille de message rend les gros fichiers (paquets de plusieurs Go) peu fiables
/// quel que soit le réglage de buffer. Voir
/// https://learn.microsoft.com/aspnet/core/blazor/file-uploads#server-side-signalr-message-size-limit.
/// </summary>
public class DeploymentPackageFileStorageService(IConfiguration configuration)
{
    // Simple copie disque-à-disque HTTP (plus de contrainte SignalR ici) : un buffer généreux
    // maximise le débit pour les gros paquets.
    private const int ReadBufferSize = 1024 * 1024;

    private const long DefaultPartSizeBytes = 5 * 1024 * 1024;

    public async Task<(string FileSha512, long TotalSizeBytes, IReadOnlyList<(string StoragePath, string Sha512, long SizeBytes)> Parts)>
        SaveAsSplitAsync(Stream input, CancellationToken cancellationToken = default)
    {
        string rootPath = RootPath();
        Directory.CreateDirectory(rootPath);

        long partSize = PartSizeBytes();
        byte[] buffer = new byte[ReadBufferSize];
        List<(string StoragePath, string Sha512, long SizeBytes)> parts = [];

        using IncrementalHash wholeFileHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        long totalSizeBytes = 0;
        int partIndex = 0;
        bool endOfStream = false;

        while (!endOfStream)
        {
            // Le nom final (sha512 du contenu du fragment) n'est connu qu'une fois le fragment
            // entièrement écrit : on écrit d'abord sous un nom temporaire, puis on renomme.
            string tempStoragePath = $"{Guid.NewGuid():N}.tmp";
            string tempFullPath = Path.Combine(rootPath, tempStoragePath);

            using IncrementalHash partHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
            long partSizeWritten = 0;

            await using (FileStream output = System.IO.File.Create(tempFullPath))
            {
                while (partSizeWritten < partSize)
                {
                    int toRead = (int)Math.Min(buffer.Length, partSize - partSizeWritten);
                    int bytesRead = await input.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken);
                    if (bytesRead == 0)
                    {
                        endOfStream = true;
                        break;
                    }

                    await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    wholeFileHash.AppendData(buffer, 0, bytesRead);
                    partHash.AppendData(buffer, 0, bytesRead);
                    partSizeWritten += bytesRead;
                    totalSizeBytes += bytesRead;
                }
            }

            if (partSizeWritten == 0)
            {
                // Rien écrit dans ce fragment (flux déjà épuisé pile à la frontière du fragment
                // précédent) : on le retire plutôt que de garder un fragment vide inutile.
                System.IO.File.Delete(tempFullPath);
                break;
            }

            string partSha512 = Convert.ToHexStringLower(partHash.GetHashAndReset());
            string partStoragePath = ShardedPartStoragePath(partSha512);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(rootPath, partStoragePath))!);
            // Deux fragments (même dans des fichiers différents) peuvent avoir un contenu
            // identique et donc le même sha512 : overwrite:true est sans risque puisque le
            // contenu est alors, par définition, le même.
            System.IO.File.Move(tempFullPath, Path.Combine(rootPath, partStoragePath), overwrite: true);

            parts.Add((partStoragePath, partSha512, partSizeWritten));
            partIndex++;
        }

        if (parts.Count == 0)
        {
            // Fichier vide : on garde tout de même un fragment (taille 0) pour rester cohérent
            // avec l'invariant "au moins une part par fichier".
            string emptyPartSha512 = Convert.ToHexStringLower(SHA512.HashData([]));
            string emptyPartPath = ShardedPartStoragePath(emptyPartSha512);
            string emptyPartFullPath = Path.Combine(rootPath, emptyPartPath);
            Directory.CreateDirectory(Path.GetDirectoryName(emptyPartFullPath)!);
            if (!System.IO.File.Exists(emptyPartFullPath))
            {
                await using System.IO.FileStream _ = System.IO.File.Create(emptyPartFullPath);
            }

            parts.Add((emptyPartPath, emptyPartSha512, 0));
        }

        string fileSha512 = Convert.ToHexStringLower(wholeFileHash.GetHashAndReset());
        return (fileSha512, totalSizeBytes, parts);
    }

    /// <summary>Réécrit un fichier reconstitué en concaténant ses fragments, dans l'ordre, vers
    /// <paramref name="destination"/> — utilisé par <c>AgentController.GetDeployFile</c> pour
    /// servir le fichier entier sans jamais le matérialiser sur disque.</summary>
    public async Task WriteReconstructedFileAsync(IEnumerable<string> orderedPartStoragePaths, Stream destination,
        CancellationToken cancellationToken = default)
    {
        byte[] buffer = new byte[ReadBufferSize];
        foreach (string storagePath in orderedPartStoragePaths)
        {
            await using FileStream input = System.IO.File.OpenRead(GetFullPath(storagePath));
            int bytesRead;
            while ((bytesRead = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            }
        }
    }

    public string GetFullPath(string storagePath) => Path.Combine(RootPath(), storagePath);

    /// <summary>Supprime du disque les fragments de <paramref name="storagePaths"/> qui ne sont
    /// plus référencés par aucun <see cref="DeploymentPackageFilePart"/> en base. Nécessaire
    /// depuis que les fragments sont nommés par leur sha512 (voir <see cref="SaveAsSplitAsync"/>) :
    /// deux fragments au contenu identique, même dans des fichiers/paquets différents, partagent
    /// désormais le même fichier physique. À appeler après (jamais avant) la suppression des
    /// lignes en base, sans quoi les lignes sur le point d'être supprimées seraient encore vues
    /// comme des références valides.</summary>
    public async Task DeleteOrphanedPartsAsync(DbContext db, IEnumerable<string> storagePaths,
        CancellationToken cancellationToken = default)
    {
        foreach (string storagePath in storagePaths.Distinct())
        {
            bool stillReferenced = await db.Set<DeploymentPackageFilePart>()
                .AnyAsync(part => part.StoragePath == storagePath, cancellationToken);
            if (stillReferenced)
            {
                continue;
            }

            string fullPath = GetFullPath(storagePath);
            if (System.IO.File.Exists(fullPath))
            {
                System.IO.File.Delete(fullPath);
            }
        }
    }

    /// <summary>Chemin relatif (sous <c>RootPath</c>) d'un fragment, réparti sur deux niveaux de
    /// dossiers d'après son sha512 pour éviter des dossiers à plusieurs centaines de milliers
    /// d'entrées : <c>0b5161778c...</c> → <c>0/0b/0b5161778c...</c>.</summary>
    private static string ShardedPartStoragePath(string sha512) => $"{sha512[..1]}/{sha512[..2]}/{sha512}";

    private string RootPath() => configuration["PackageStorage:RootPath"] ?? "PackageStorage";

    private long PartSizeBytes() => configuration.GetValue<long?>("PackageStorage:PartSizeBytes") ?? DefaultPartSizeBytes;
}
