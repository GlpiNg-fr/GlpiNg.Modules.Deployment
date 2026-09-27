using GlpiNg.Modules.Deployment.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using GlpiNg.Modules.Abstractions.Localization;

namespace GlpiNg.Modules.Deployment.Components.Pages.Deployments;

public partial class Supervision : ComponentBase, IDisposable
{
    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    private List<DeploymentJob> _jobs = [];
    private int? _expandedJobId;
    private CancellationTokenSource? _pollCts;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        await using DbContext db = await DbFactory.CreateDbContextAsync();
        _jobs = await db.Set<DeploymentJob>()
            .AsNoTracking()
            .Include(j => j.Agent)
            .Include(j => j.Package)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync();

        EnsurePollingIfNeeded();
    }

    // Tant qu'au moins un job affiché n'est pas terminé (Pending/Running), interroge la base
    // toutes les PollInterval pour refléter la progression rapportée par l'agent via setStatus
    // (voir AgentController.HandleSetStatusAsync) sans que l'admin ait à rafraîchir la page à la
    // main. Idempotente : appelée après chaque LoadAsync (y compris depuis la boucle de poll
    // elle-même), elle ne relance pas de boucle si une tourne déjà (_pollCts non nul) — la boucle
    // en cours s'arrête d'elle-même (voir PollAsync) une fois qu'aucun job n'est plus en attente.
    private void EnsurePollingIfNeeded()
    {
        if (_pollCts is not null || !_jobs.Exists(j => j.Status is DeploymentStatus.Pending or DeploymentStatus.Running))
        {
            return;
        }

        _pollCts = new CancellationTokenSource();
        _ = PollAsync(_pollCts.Token);
    }

    private async Task PollAsync(CancellationToken token)
    {
        try
        {
            using PeriodicTimer timer = new(PollInterval);
            while (await timer.WaitForNextTickAsync(token))
            {
                await InvokeAsync(async () =>
                {
                    await LoadAsync();
                    StateHasChanged();
                });

                if (!_jobs.Exists(j => j.Status is DeploymentStatus.Pending or DeploymentStatus.Running))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _pollCts?.Cancel();
            _pollCts?.Dispose();
            _pollCts = null;
        }
    }

    public void Dispose()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
    }

    private void ToggleLog(int jobId)
    {
        _expandedJobId = _expandedJobId == jobId ? null : jobId;
    }

    private static string StatusLabel(DeploymentStatus status) => status switch
    {
        DeploymentStatus.Pending => Tr.T("En attente"),
        DeploymentStatus.Running => Tr.T("En cours"),
        DeploymentStatus.Success => Tr.T("Réussi"),
        DeploymentStatus.Error => Tr.T("En erreur"),
        _ => status.ToString()
    };

    private static string StatusBadgeClass(DeploymentStatus status) => status switch
    {
        DeploymentStatus.Pending => "bg-secondary",
        DeploymentStatus.Running => "bg-azure",
        DeploymentStatus.Success => "bg-success",
        DeploymentStatus.Error => "bg-danger",
        _ => "bg-secondary"
    };
}
