using GlpiNg.Modules.Deployment.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace GlpiNg.Modules.Deployment.Components.Pages.Deployments;

public partial class ComputerGroups : ComponentBase
{
    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    private List<DeployComputerGroup> _groups = [];
    private readonly HashSet<int> _selectedIds = [];
    private DeployComputerGroup _newGroup = new() { Name = string.Empty };

    private bool AllSelected => _groups.Count > 0 && _selectedIds.Count == _groups.Count;

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        _groups = await db.Set<DeployComputerGroup>()
            .AsNoTracking()
            .Include(g => g.Members)
            .OrderBy(g => g.Name)
            .ToListAsync();
        _selectedIds.Clear();
    }

    private void ToggleSelectAll(bool selectAll)
    {
        _selectedIds.Clear();
        if (selectAll)
        {
            foreach (DeployComputerGroup group in _groups)
            {
                _selectedIds.Add(group.Id);
            }
        }
    }

    private void ToggleSelect(int id, bool selected)
    {
        if (selected) _selectedIds.Add(id);
        else _selectedIds.Remove(id);
    }

    private async Task DeleteSelectedAsync()
    {
        if (_selectedIds.Count == 0) return;

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        List<DeployComputerGroup> toDelete = await db.Set<DeployComputerGroup>().Where(g => _selectedIds.Contains(g.Id)).ToListAsync();
        db.Set<DeployComputerGroup>().RemoveRange(toDelete);
        await db.SaveChangesAsync();

        await LoadAsync();
    }

    private async Task CreateGroupAsync()
    {
        if (string.IsNullOrWhiteSpace(_newGroup.Name))
        {
            return;
        }

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        db.Set<DeployComputerGroup>().Add(_newGroup);
        await db.SaveChangesAsync();

        _newGroup = new DeployComputerGroup { Name = string.Empty };
        await JS.InvokeVoidAsync("glpiNg.hideModal", "newComputerGroupModal");
        await LoadAsync();
    }

    private static string TypeLabel(DeployComputerGroupType type) =>
        type == DeployComputerGroupType.Dynamic ? "Groupe dynamique" : "Groupe statique";
}
