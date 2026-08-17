using GlpiNg.Modules.Abstractions.Menu;

namespace GlpiNg.Modules.Deployment;

/// <summary>
/// Contribue l'entrée "Déploiements" du groupe "Outils" (voir aussi
/// InventoryMenuProvider, qui contribue "Recherches sauvegardées" au même groupe —
/// les entrées de plusieurs IMenuProvider partageant une clé de groupe sont fusionnées
/// par l'hôte) ainsi que le groupe "Libre-service" (page /self-service, ouverte à tout
/// utilisateur connecté — contrairement à "Outils", pensé pour les techniciens).
/// </summary>
public sealed class DeploymentMenuProvider : IMenuProvider
{
    public IReadOnlyList<MenuGroup> GetMenuGroups() =>
    [
        new("outils", "ti-briefcase", "Outils",
        [
            new("Déploiements", "/tools/deployments", "ti-rocket"),
        ]),
        new("libre-service", "ti-shopping-cart", "Libre-service",
        [
            new("Déploiement de paquets", "/self-service", "ti-download"),
        ]),
    ];
}
