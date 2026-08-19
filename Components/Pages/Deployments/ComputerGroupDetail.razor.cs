using GlpiNg.Modules.Inventory.Models;
using GlpiNg.Modules.Deployment.Models;
using GlpiNg.Modules.Deployment.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Deployment.Components.Pages.Deployments;

public partial class ComputerGroupDetail : ComponentBase, IAsyncDisposable
{
    [Parameter]
    public int GroupId { get; set; }

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private NavigationManager Nav { get; set; } = null!;

    private DbContext? _db;
    private DeployComputerGroup? _group;
    private List<Computer> _availableComputers = [];
    private List<Computer> _dynamicMatches = [];
    private int _loadedId;
    private bool _isSaving;
    private string _activeTabKey = "main";
    private int _computerIdToAdd;

    private DeployComputerGroupCriterion _newCriterion = new();
    private readonly Dictionary<DeployCriterionField, List<string>> _distinctValuesCache = [];

    protected override async Task OnParametersSetAsync()
    {
        if (_group is not null && _loadedId == GroupId)
        {
            return;
        }

        _loadedId = GroupId;
        _activeTabKey = "main";
        _distinctValuesCache.Clear();

        if (_db is not null)
        {
            await _db.DisposeAsync();
        }

        _db = await DbFactory.CreateDbContextAsync();
        _group = await _db.Set<DeployComputerGroup>()
            .Include(g => g.Members)
            .ThenInclude(m => m.Computer)
            .Include(g => g.Criteria)
            .FirstOrDefaultAsync(g => g.Id == GroupId);

        await ReloadAvailableComputersAsync();
        await ReloadDynamicMatchesAsync();
    }

    private async Task ReloadAvailableComputersAsync()
    {
        if (_db is null || _group is null) return;

        HashSet<int> memberComputerIds = _group.Members.Select(m => m.ComputerId).ToHashSet();
        _availableComputers = await _db.Set<Computer>()
            .AsNoTracking()
            .Where(c => !memberComputerIds.Contains(c.Id))
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    private async Task ReloadDynamicMatchesAsync()
    {
        if (_db is null || _group is null || _group.Type != DeployComputerGroupType.Dynamic)
        {
            _dynamicMatches = [];
            return;
        }

        List<Computer> allComputers = await _db.Set<Computer>().AsNoTracking().Include(c => c.StatusItem).ToListAsync();
        _dynamicMatches = DeployGroupCriteriaEvaluator.Filter(allComputers, _group.Criteria)
            .OrderBy(c => c.Name)
            .ToList();
    }

    private void SetTab(string key) => _activeTabKey = key;

    private async Task SaveAsync()
    {
        if (_db is null || _group is null) return;

        _isSaving = true;
        try
        {
            await _db.SaveChangesAsync();
            await ReloadDynamicMatchesAsync();
        }
        finally
        {
            _isSaving = false;
        }
    }

    private async Task DeleteAsync()
    {
        if (_db is null || _group is null) return;

        _db.Set<DeployComputerGroup>().Remove(_group);
        await _db.SaveChangesAsync();
        Nav.NavigateTo("/tools/deployments/computer-groups");
    }

    private async Task AddMemberAsync()
    {
        if (_db is null || _group is null || _computerIdToAdd == 0) return;

        _group.Members.Add(new DeployComputerGroupMember
        {
            DeployComputerGroupId = _group.Id,
            ComputerId = _computerIdToAdd
        });
        await _db.SaveChangesAsync();

        _computerIdToAdd = 0;
        await _db.Entry(_group).Collection(g => g.Members).LoadAsync();
        foreach (DeployComputerGroupMember member in _group.Members)
        {
            await _db.Entry(member).Reference(m => m.Computer).LoadAsync();
        }
        await ReloadAvailableComputersAsync();
    }

    private async Task RemoveMemberAsync(DeployComputerGroupMember member)
    {
        if (_db is null || _group is null) return;

        _group.Members.Remove(member);
        _db.Set<DeployComputerGroupMember>().Remove(member);
        await _db.SaveChangesAsync();
        await ReloadAvailableComputersAsync();
    }

    private async Task AddCriterionAsync()
    {
        if (_db is null || _group is null) return;
        if (_newCriterion.Operator != DeployCriterionOperator.IsEmpty && string.IsNullOrWhiteSpace(_newCriterion.Value)) return;

        _newCriterion.DeployComputerGroupId = _group.Id;
        _newCriterion.SortOrder = _group.Criteria.Count;
        _group.Criteria.Add(_newCriterion);
        await _db.SaveChangesAsync();

        _newCriterion = new DeployComputerGroupCriterion();
        await ReloadDynamicMatchesAsync();
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

    private async Task RemoveCriterionAsync(DeployComputerGroupCriterion criterion)
    {
        if (_db is null || _group is null) return;

        _group.Criteria.Remove(criterion);
        _db.Set<DeployComputerGroupCriterion>().Remove(criterion);
        await _db.SaveChangesAsync();
        await ReloadDynamicMatchesAsync();
    }

    private static string TypeLabel(DeployComputerGroupType type) =>
        type == DeployComputerGroupType.Dynamic ? "Groupe dynamique" : "Groupe statique";

    private static string FieldLabel(DeployCriterionField field) => field switch
    {
        DeployCriterionField.Name => "Nom",
        DeployCriterionField.SerialNumber => "Numéro de série",
        DeployCriterionField.Manufacturer => "Fabricant",
        DeployCriterionField.Model => "Modèle",
        DeployCriterionField.OperatingSystem => "Système d'exploitation",
        DeployCriterionField.OsVersion => "Version de l'OS",
        DeployCriterionField.Status => "Statut",
        DeployCriterionField.Site => "Site",
        DeployCriterionField.Building => "Bâtiment",
        DeployCriterionField.Room => "Salle",
        DeployCriterionField.AssignedUser => "Utilisateur",
        _ => field.ToString()
    };

    private static string OperatorLabel(DeployCriterionOperator op) => op switch
    {
        DeployCriterionOperator.Contains => "contient",
        DeployCriterionOperator.NotContains => "ne contient pas",
        DeployCriterionOperator.Is => "est",
        DeployCriterionOperator.IsNot => "n'est pas",
        DeployCriterionOperator.IsEmpty => "est vide",
        _ => op.ToString()
    };

    private static string LinkLabel(DeployCriterionLink link) => link == DeployCriterionLink.Or ? "OU" : "ET";

    public async ValueTask DisposeAsync()
    {
        if (_db is not null)
        {
            await _db.DisposeAsync();
        }
    }
}
