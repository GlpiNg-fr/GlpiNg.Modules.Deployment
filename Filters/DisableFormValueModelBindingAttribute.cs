using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace GlpiNg.Modules.Deployment.Filters;

/// <summary>
/// Empêche MVC de composer un <c>FormValueProviderFactory</c> (ni <c>FormFileValueProviderFactory</c>,
/// <c>JQueryFormValueProviderFactory</c>) pour l'action ciblée. Sans ça, dès qu'une requête POST a un
/// Content-Type "multipart/form-data", l'infrastructure de model binding de MVC appelle elle-même
/// <c>Request.ReadFormAsync()</c> pour préparer ses value providers — <em>avant même que le corps de
/// l'action ne s'exécute</em>, et ce même si aucun paramètre de l'action n'est lié depuis le formulaire
/// (voir <see cref="Controllers.DeploymentPackageFilesController.UploadAsync"/>, dont le seul paramètre
/// lié au form-data est <c>packageId</c>, résolu depuis la route). Sur un gros fichier, ce
/// <c>ReadFormAsync()</c> "invisible" épuise entièrement le flux de la requête (plusieurs secondes),
/// avant même que le <c>MultipartReader</c> du contrôleur n'ait pu lire quoi que ce soit — d'où
/// l'erreur "Unexpected end of Stream, the content may have already been read by another component."
/// Solution documentée par Microsoft pour le streaming d'upload volumineux, voir
/// https://learn.microsoft.com/aspnet/core/mvc/models/file-uploads#upload-large-files-with-streaming.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class DisableFormValueModelBindingAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        IList<IValueProviderFactory> factories = context.ValueProviderFactories;
        factories.RemoveType<FormValueProviderFactory>();
        factories.RemoveType<FormFileValueProviderFactory>();
        factories.RemoveType<JQueryFormValueProviderFactory>();
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
