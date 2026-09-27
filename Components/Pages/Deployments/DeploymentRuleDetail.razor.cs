using GlpiNg.Modules.Abstractions.Deployment;
using GlpiNg.Modules.Deployment.Models;
using GlpiNg.Modules.Deployment.Services;
using GlpiNg.Modules.Inventory.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using GlpiNg.Modules.Abstractions.Localization;

namespace GlpiNg.Modules.Deployment.Components.Pages.Deployments;

public partial class DeploymentRuleDetail : ComponentBase, IAsyncDisposable
{
    [Parameter]
    public int RuleId { get; set; }

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private IComputerDeploymentAssignmentService AssignmentService { get; set; } = null!;

    [Inject]
    private NavigationManager Nav { get; set; } = null!;

    private DbContext? _db;
    private DeploymentRule? _rule;
    private List<DeploymentPackage> _availablePackages = [];
    private List<Computer> _matches = [];
    private int _loadedId;
    private bool _isSaving;
    private string _activeTabKey = "main";

    private DeploymentRuleCriterion _newCriterion = new();
    private int _packageIdToAdd;
    private readonly Dictionary<DeployCriterionField, List<string>> _distinctValuesCache = [];

    private bool _isEvaluating;
    private string? _evaluationResultMessage;

    protected override async Task OnParametersSetAsync()
    {
        if (_rule is not null && _loadedId == RuleId)
        {
            return;
        }

        _loadedId = RuleId;
        _activeTabKey = "main";
        _distinctValuesCache.Clear();
        _evaluationResultMessage = null;
        _newCriterion = new DeploymentRuleCriterion();

        if (_db is not null)
        {
            await _db.DisposeAsync();
        }

        _db = await DbFactory.CreateDbContextAsync();
        _rule = await _db.Set<DeploymentRule>()
            .Include(r => r.Criteria)
            .Include(r => r.Actions)
            .ThenInclude(a => a.Package)
            .FirstOrDefaultAsync(r => r.Id == RuleId);

        await ReloadAvailablePackagesAsync();
        await ReloadMatchesAsync();
    }

    private async Task ReloadAvailablePackagesAsync()
    {
        if (_db is null || _rule is null) return;

        HashSet<int> assignedPackageIds = _rule.Actions.Select(a => a.PackageId).ToHashSet();
        _availablePackages = await _db.Set<DeploymentPackage>()
            .AsNoTracking()
            .Where(p => p.SupersededByPackageId == null && !assignedPackageIds.Contains(p.Id))
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    private async Task ReloadMatchesAsync()
    {
        if (_db is null || _rule is null)
        {
            _matches = [];
            return;
        }

        List<Computer> allComputers = await _db.Set<Computer>().AsNoTracking().Include(c => c.StatusItem).ToListAsync();
        _matches = DeployGroupCriteriaEvaluator.Filter(allComputers, _rule.Criteria).OrderBy(c => c.Name).ToList();
    }

    private void SetTab(string key) => _activeTabKey = key;

    private async Task SaveAsync()
    {
        if (_db is null || _rule is null) return;

        _isSaving = true;
        try
        {
            await _db.SaveChangesAsync();
        }
        finally
        {
            _isSaving = false;
        }
    }

    private async Task DeleteAsync()
    {
        if (_db is null || _rule is null) return;

        _db.Set<DeploymentRule>().Remove(_rule);
        await _db.SaveChangesAsync();
        Nav.NavigateTo("/tools/deployments/rules");
    }

    private async Task AddCriterionAsync()
    {
        if (_db is null || _rule is null) return;
        if (_newCriterion.Operator != DeployCriterionOperator.IsEmpty && string.IsNullOrWhiteSpace(_newCriterion.Value)) return;

        _newCriterion.DeploymentRuleId = _rule.Id;
        _newCriterion.SortOrder = _rule.Criteria.Count;
        _rule.Criteria.Add(_newCriterion);
        await _db.SaveChangesAsync();

        _newCriterion = new DeploymentRuleCriterion();
        await ReloadMatchesAsync();
    }

    private async Task OnCriterionFieldChanged()
    {
        _newCriterion.Value = "";
        await EnsureDistinctValuesLoadedAsync();
    }

    private async Task OnCriterionOperatorChanged()
    {
        _newCriterion.Value = "";
        await EnsureDistinctValuesLoadedAsync();
    }

    private async Task EnsureDistinctValuesLoadedAsync()
    {
        DeployCriterionField field = _newCriterion.Field;
        if (_db is null || _distinctValuesCache.ContainsKey(field))
        {
            return;
        }
        if (_newCriterion.Operator is not (DeployCriterionOperator.Is or DeployCriterionOperator.IsNot))
        {
            return;
        }

        IQueryable<string?>? values = field switch
        {
            DeployCriterionField.Name => _db.Set<Computer>().Select(c => (string?)c.Name),
            DeployCriterionField.SerialNumber => _db.Set<Computer>().Select(c => c.SerialNumber),
            DeployCriterionField.Status => _db.Set<Computer>().Select(c => c.StatusItem != null ? c.StatusItem.Name : null),
            DeployCriterionField.Manufacturer => _db.Set<Computer>().Select(c => c.Manufacturer),
            DeployCriterionField.Model => _db.Set<Computer>().Select(c => c.Model),
            DeployCriterionField.OperatingSystem => _db.Set<Computer>().Select(c => c.OperatingSystem),
            DeployCriterionField.OsVersion => _db.Set<Computer>().Select(c => c.OsVersion),
            DeployCriterionField.Site => _db.Set<Computer>().Select(c => c.Site),
            DeployCriterionField.Building => _db.Set<Computer>().Select(c => c.Building),
            DeployCriterionField.Room => _db.Set<Computer>().Select(c => c.Room),
            DeployCriterionField.AssignedUser => _db.Set<Computer>().Select(c => c.AssignedUser),
            _ => null
        };
        if (values is null)
        {
            return;
        }

        _distinctValuesCache[field] = await values
            .Where(v => v != null && v != "")
            .Distinct()
            .OrderBy(v => v)
            .Select(v => v!)
            .ToListAsync();
    }

    private async Task RemoveCriterionAsync(DeploymentRuleCriterion criterion)
    {
        if (_db is null || _rule is null) return;

        _rule.Criteria.Remove(criterion);
        _db.Set<DeploymentRuleCriterion>().Remove(criterion);
        await _db.SaveChangesAsync();
        await ReloadMatchesAsync();
    }

    private async Task AddActionAsync()
    {
        if (_db is null || _rule is null || _packageIdToAdd == 0) return;

        _rule.Actions.Add(new DeploymentRuleAction { DeploymentRuleId = _rule.Id, PackageId = _packageIdToAdd });
        await _db.SaveChangesAsync();

        _packageIdToAdd = 0;
        await _db.Entry(_rule).Collection(r => r.Actions).LoadAsync();
        foreach (DeploymentRuleAction action in _rule.Actions)
        {
            await _db.Entry(action).Reference(a => a.Package).LoadAsync();
        }
        await ReloadAvailablePackagesAsync();
    }

    private async Task RemoveActionAsync(DeploymentRuleAction action)
    {
        if (_db is null || _rule is null) return;

        _rule.Actions.Remove(action);
        _db.Set<DeploymentRuleAction>().Remove(action);
        await _db.SaveChangesAsync();
        await ReloadAvailablePackagesAsync();
    }

    /// <summary>
    /// Applique la règle telle qu'actuellement enregistrée à tout le parc existant : assigne les
    /// paquets de <see cref="_rule"/> aux ordinateurs correspondants qui ne les ont pas déjà (voir
    /// DeploymentRuleEngine.ResolveNewPackageIdsAsync). Complète l'évaluation automatique à
    /// l'inventaire (InventoryImportService), qui ne couvre que les imports à venir.
    /// </summary>
    private async Task EvaluateNowAsync()
    {
        if (_db is null || _rule is null) return;

        _isEvaluating = true;
        _evaluationResultMessage = null;
        try
        {
            int assignedComputers = 0;
            foreach (Computer computer in _matches)
            {
                if (computer.AgentId is not { } agentId) continue;

                List<int> newPackageIds = await DeploymentRuleEngine.ResolveNewPackageIdsAsync(_db, computer, agentId, [_rule]);
                if (newPackageIds.Count == 0) continue;

                await AssignmentService.AssignPackagesAsync(computer.Id, newPackageIds);
                assignedComputers++;
            }

            _evaluationResultMessage = _matches.Count == 0
                ? Tr.T("Aucun ordinateur ne correspond actuellement à ces critères.")
                : Tr.T("{0} ordinateur(s) correspondant(s), {1} nouvelle(s) assignation(s).", _matches.Count, assignedComputers);
        }
        finally
        {
            _isEvaluating = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }
    }
}
