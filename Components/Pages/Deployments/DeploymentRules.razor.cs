using GlpiNg.Modules.Deployment.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;

namespace GlpiNg.Modules.Deployment.Components.Pages.Deployments;

public partial class DeploymentRules : ComponentBase
{
    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private NavigationManager Nav { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    private List<DeploymentRule> _rules = [];
    private readonly HashSet<int> _selectedIds = [];
    private DeploymentRule _newRule = new() { Name = string.Empty };

    private bool AllSelected => _rules.Count > 0 && _selectedIds.Count == _rules.Count;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        _rules = await db.Set<DeploymentRule>()
            .AsNoTracking()
            .Include(r => r.Criteria)
            .Include(r => r.Actions)
            .ThenInclude(a => a.Package)
            .OrderBy(r => r.SortOrder)
            .ToListAsync();
        _selectedIds.Clear();
    }

    private void ToggleSelectAll(bool selectAll)
    {
        _selectedIds.Clear();
        if (selectAll)
        {
            foreach (DeploymentRule rule in _rules)
            {
                _selectedIds.Add(rule.Id);
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
        List<DeploymentRule> toDelete = await db.Set<DeploymentRule>().Where(r => _selectedIds.Contains(r.Id)).ToListAsync();
        db.Set<DeploymentRule>().RemoveRange(toDelete);
        await db.SaveChangesAsync();

        await LoadAsync();
    }

    private async Task ToggleActiveAsync(DeploymentRule rule)
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        DeploymentRule? tracked = await db.Set<DeploymentRule>().FirstOrDefaultAsync(r => r.Id == rule.Id);
        if (tracked is null) return;

        tracked.IsActive = !tracked.IsActive;
        await db.SaveChangesAsync();
        await LoadAsync();
    }

    private async Task MoveAsync(DeploymentRule rule, int direction)
    {
        int index = _rules.FindIndex(r => r.Id == rule.Id);
        int swapIndex = index + direction;
        if (index < 0 || swapIndex < 0 || swapIndex >= _rules.Count) return;

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        DeploymentRule? a = await db.Set<DeploymentRule>().FirstOrDefaultAsync(r => r.Id == _rules[index].Id);
        DeploymentRule? b = await db.Set<DeploymentRule>().FirstOrDefaultAsync(r => r.Id == _rules[swapIndex].Id);
        if (a is null || b is null) return;

        (a.SortOrder, b.SortOrder) = (b.SortOrder, a.SortOrder);
        await db.SaveChangesAsync();
        await LoadAsync();
    }

    private async Task CreateRuleAsync()
    {
        if (string.IsNullOrWhiteSpace(_newRule.Name)) return;

        await using DbContext db = await DbFactory.CreateDbContextAsync();
        _newRule.SortOrder = await db.Set<DeploymentRule>().CountAsync();
        db.Set<DeploymentRule>().Add(_newRule);
        await db.SaveChangesAsync();

        int newId = _newRule.Id;
        _newRule = new DeploymentRule { Name = string.Empty };
        await JS.InvokeVoidAsync("glpiNg.hideModal", "newDeploymentRuleModal");
        Nav.NavigateTo($"/tools/deployments/rules/{newId}");
    }
}
