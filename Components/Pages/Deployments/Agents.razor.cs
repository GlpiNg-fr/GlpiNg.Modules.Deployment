using GlpiNg.Modules.Inventory.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;

namespace GlpiNg.Modules.Deployment.Components.Pages.Deployments;

public partial class Agents : ComponentBase
{
    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    private List<GlpiAgent> _agents = [];
    private List<GlpiAgent> _filteredAgents = [];
    private string _searchTerm = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        _agents = await db.Set<GlpiAgent>()
            .AsNoTracking()
            .Include(agent => agent.Computer)
            .OrderByDescending(agent => agent.LastContactAt)
            .ToListAsync();

        _filteredAgents = _agents;
    }

    private void OnSearchChanged(KeyboardEventArgs args)
    {
        string term = _searchTerm.Trim();

        _filteredAgents = term.Length == 0
            ? _agents
            : _agents.Where(agent => MatchesSearch(agent, term)).ToList();
    }

    private static bool MatchesSearch(GlpiAgent agent, string term)
    {
        return AgentDisplayName(agent).Contains(term, StringComparison.OrdinalIgnoreCase)
            || (agent.Tag?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
            || (agent.DeviceId?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
            || (agent.AgentVersion?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
            || (agent.Computer?.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static string AgentDisplayName(GlpiAgent agent) =>
        agent.AgentName ?? agent.DeviceId ?? agent.AgentUuid;

    private string LastContactLabel(GlpiAgent agent) =>
        Display.DateTime(agent.LastContactAt)!;
}
