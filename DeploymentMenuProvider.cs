using GlpiNg.Modules.Abstractions.Menu;

namespace GlpiNg.Modules.Deployment;

/// <summary>
/// Contribue l'entrée "Déploiements" du groupe "Outils" (voir aussi
/// InventoryMenuProvider, qui contribue "Recherches sauvegardées" au même groupe —
/// les entrées de plusieurs IMenuProvider partageant une clé de groupe sont fusionnées
/// par l'hôte).
/// </summary>
public sealed class DeploymentMenuProvider : IMenuProvider
{
    public IReadOnlyList<MenuGroup> GetMenuGroups() =>
    [
        // Contribué au groupe "parc" et non "outils" : l'actif non géré est un objet de parc, même
        // si la donnée (DiscoveredNetworkDevice) est alimentée par la découverte réseau de ce
        // module. L'entrée était jusqu'ici un placeholder du menu hôte.
        new("parc", "ti-box", "Parc",
        [
            new("Actifs non gérés", "/parc/unmanaged", "ti-help"),
        ]),
        new("outils", "ti-briefcase", "Outils",
        [
            new("Déploiements", "/tools/deployments", "ti-rocket"),
        ]),
    ];
}
