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
        new("outils", "ti-briefcase", "Outils",
        [
            new("Déploiements", "/tools/deployments", "ti-rocket"),
        ]),
    ];
}
