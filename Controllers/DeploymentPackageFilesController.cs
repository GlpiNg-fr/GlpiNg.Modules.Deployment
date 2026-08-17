using GlpiNg.Modules.Deployment.Filters;
using GlpiNg.Modules.Deployment.Models;
using GlpiNg.Modules.Deployment.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace GlpiNg.Modules.Deployment.Controllers;

/// <summary>
/// Upload de fichiers de paquet de déploiement, un fichier par requête (voir
/// <c>glpiNg.uploadPackageFiles</c> dans glpi-ng.js, appelé depuis
/// <c>Components/Pages/Deployments/Detail.razor</c>).
///
/// Volontairement une requête HTTP multipart classique plutôt qu'un composant Blazor
/// <c>InputFile</c> : ce dernier fait transiter les octets par le circuit SignalR, peu fiable
/// pour de gros fichiers (paquets de plusieurs Go) — voir <see cref="DeploymentPackageFileStorageService"/>.
/// Protégé par le FallbackPolicy global (cookie de session applicative requis, voir Program.cs) ;
/// aucun jeton CSRF explicite, la protection reposant sur SameSite=Lax des cookies d'authentification
/// (une requête POST cross-site ne porte pas le cookie), comme pour le reste de l'admin.
///
/// [IgnoreAntiforgeryToken] : depuis .NET 8, UseAntiforgery() valide automatiquement le jeton
/// anti-CSRF sur toute action MVC POST/PUT/DELETE/PATCH sans opt-out explicite — cette requête
/// XHR n'en porte pas (elle ne passe pas par un &lt;form&gt;/&lt;AntiforgeryToken&gt; Blazor).
///
/// [DisableFormValueModelBinding] : le vrai correctif de l'erreur "Unexpected end of Stream, the
/// content may have already been read by another component." Sans lui, dès qu'une requête POST a
/// un Content-Type "multipart/form-data", l'infrastructure de model binding de MVC compose
/// elle-même un <c>FormValueProviderFactory</c> et appelle <c>Request.ReadFormAsync()</c> pour
/// préparer ses value providers — *avant même que le corps de l'action ci-dessous ne s'exécute*,
/// et ce même si aucun paramètre de l'action n'est lié depuis le formulaire (seul <c>packageId</c>
/// est lié, depuis la route). Sur un gros fichier, ce <c>ReadFormAsync()</c> invisible épuise
/// entièrement le flux de la requête (plusieurs secondes) avant même que le <c>MultipartReader</c>
/// ci-dessous n'ait pu lire quoi que ce soit. Voir <see cref="DisableFormValueModelBindingAttribute"/>.
/// </summary>
[ApiController]
[Route("deployment-packages")]
[IgnoreAntiforgeryToken]
public class DeploymentPackageFilesController(
    DbContext db,
    DeploymentPackageFileStorageService fileStorage) : ControllerBase
{
    // Couvre les 4 Go annoncés par l'utilisateur avec de la marge.
    private const long MaxFileSize = 8L * 1024 * 1024 * 1024;

    [HttpPost("{packageId:int}/files")]
    [RequestSizeLimit(MaxFileSize)]
    [DisableFormValueModelBinding]
    public async Task<IActionResult> UploadAsync(int packageId, CancellationToken cancellationToken)
    {
        // [RequestSizeLimit] seul ne suffit pas avec Kestrel : il faut aussi lever la limite au
        // niveau de la feature de transport, sans quoi la connexion est coupée dès 30 Mo environ
        // (KestrelServerLimits.MaxRequestBodySize par défaut) avant même d'atteindre l'action.
        IHttpMaxRequestBodySizeFeature? sizeFeature = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false })
        {
            sizeFeature.MaxRequestBodySize = MaxFileSize;
        }

        // Kestrel vérifie chaque seconde que les octets arrivent à au moins 240 o/s
        // (KestrelServerLimits.MinRequestBodyDataRate par défaut) et coupe la connexion sinon —
        // sur un gros fichier (plusieurs centaines de Mo à plusieurs Go), le temps passé côté
        // serveur à écrire chaque fragment sur disque et à le hasher (SaveAsSplitAsync) peut
        // ponctuellement chuter le débit de lecture en dessous de ce seuil. Désactivée uniquement
        // pour cette requête plutôt que globalement, afin de garder la protection anti-slow-POST
        // par défaut sur le reste de l'application.
        IHttpMinRequestBodyDataRateFeature? minRateFeature = HttpContext.Features.Get<IHttpMinRequestBodyDataRateFeature>();
        if (minRateFeature is not null)
        {
            minRateFeature.MinDataRate = null;
        }

        if (string.IsNullOrEmpty(Request.ContentType)
            || !Request.ContentType.Contains("multipart/", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Requête multipart attendue.");
        }

        bool packageExists = await db.Set<DeploymentPackage>().AnyAsync(p => p.Id == packageId, cancellationToken);
        if (!packageExists)
        {
            return NotFound();
        }

        MediaTypeHeaderValue mediaType = MediaTypeHeaderValue.Parse(Request.ContentType);
        string boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary.Value!).Value
            ?? throw new InvalidOperationException("Boundary multipart manquant.");

        MultipartReader reader = new(boundary, Request.Body);
        MultipartSection? section = await reader.ReadNextSectionAsync(cancellationToken);

        while (section is not null)
        {
            if (ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out ContentDispositionHeaderValue? disposition)
                && disposition.DispositionType.Equals("form-data")
                && !string.IsNullOrEmpty(disposition.FileName.Value))
            {
                string fileName = disposition.FileName.Value;
                (string fileSha512, long sizeBytes, IReadOnlyList<(string StoragePath, string Sha512, long SizeBytes)> parts) =
                    await fileStorage.SaveAsSplitAsync(section.Body, cancellationToken);

                DeploymentPackageFile packageFile = new()
                {
                    DeploymentPackageId = packageId,
                    FileName = fileName,
                    Sha512 = fileSha512,
                    SizeBytes = sizeBytes
                };

                for (int partIndex = 0; partIndex < parts.Count; partIndex++)
                {
                    packageFile.Parts.Add(new DeploymentPackageFilePart
                    {
                        PartIndex = partIndex,
                        Sha512 = parts[partIndex].Sha512,
                        SizeBytes = parts[partIndex].SizeBytes,
                        StoragePath = parts[partIndex].StoragePath
                    });
                }

                db.Set<DeploymentPackageFile>().Add(packageFile);
                await db.SaveChangesAsync(cancellationToken);

                return Ok(new { fileName, sha512 = fileSha512, sizeBytes, partCount = parts.Count });
            }

            section = await reader.ReadNextSectionAsync(cancellationToken);
        }

        return BadRequest("Aucun fichier trouvé dans la requête.");
    }
}
