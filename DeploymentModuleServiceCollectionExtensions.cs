using GlpiNg.Modules.Abstractions.Deployment;
using GlpiNg.Modules.Abstractions.Menu;
using GlpiNg.Modules.Deployment.Controllers;
using GlpiNg.Modules.Deployment.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GlpiNg.Modules.Deployment;

/// <summary>
/// Point d'enregistrement du module Deployment dans le conteneur DI de l'hôte, même
/// principe que <c>InventoryModuleServiceCollectionExtensions.AddInventoryModule</c>.
/// Dépend du DbContext de base (via <see cref="ComputerDeploymentTasksProvider"/> et
/// les contrôleurs/pages du module) donc doit être appelé après que l'hôte a
/// enregistré son DbContext concret — voir Program.cs.
/// </summary>
public static class DeploymentModuleServiceCollectionExtensions
{
    public static IServiceCollection AddDeploymentModule(this IServiceCollection services)
    {
        // Construction du JSON de job de déploiement au format attendu par GLPI-Agent.
        services.AddSingleton<DeployJobJsonBuilder>();

        // Écriture sur disque des fichiers de paquet uploadés depuis /tools/deployments
        // (voir PackageStorage:RootPath, lu aussi par AgentController.GetDeployFile).
        services.AddScoped<DeploymentPackageFileStorageService>();

        // Lancement d'une DeploymentTask (page /tools/deployments/tasks) : crée les
        // DeploymentJob pour tous les agents du groupe cible — voir DeploymentTaskLaunchService.
        services.AddScoped<DeploymentTaskLaunchService>();

        // Alimente l'onglet "Tâches / groupes" de la fiche Ordinateur (module Inventory) sans
        // que celui-ci dépende du module Déploiement — voir IComputerDeploymentTasksProvider
        // (GlpiNg.Modules.Abstractions), même principe qu'IMenuProvider.
        services.AddScoped<IComputerDeploymentTasksProvider, ComputerDeploymentTasksProvider>();

        // Alimente l'onglet "Déploiement de package" de la fiche Ordinateur (module Inventory) :
        // liste des paquets assignables, assignation/annulation — voir
        // IComputerDeploymentAssignmentService (GlpiNg.Modules.Abstractions), même principe.
        services.AddScoped<IComputerDeploymentAssignmentService, ComputerDeploymentAssignmentService>();

        // Alimente la page /self-service (libre-service) : paquets ouverts au libre-service
        // pour lesquels l'utilisateur connecté est éligible, poste(s) sur lesquels il peut les
        // demander, et la demande elle-même — voir SelfServiceDeploymentService, qui délègue à
        // IComputerDeploymentAssignmentService ci-dessus pour l'assignation proprement dite.
        services.AddScoped<SelfServiceDeploymentService>();

        // Contribution du module au menu latéral de l'hôte (entrée "Déploiements" du
        // groupe "Outils") — voir DeploymentMenuProvider.
        services.AddSingleton<IMenuProvider, DeploymentMenuProvider>();

        // Permet à ASP.NET Core de découvrir les contrôleurs de ce module (assembly
        // distincte de celle du projet hôte).
        services.AddControllers()
            .AddApplicationPart(typeof(DeploymentPackageFilesController).Assembly);

        return services;
    }
}
