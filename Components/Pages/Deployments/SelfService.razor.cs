using System.Security.Claims;
using GlpiNg.Modules.Abstractions.Deployment;
using GlpiNg.Modules.Deployment.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace GlpiNg.Modules.Deployment.Components.Pages.Deployments;

public partial class SelfService : ComponentBase
{
    [Inject]
    private SelfServiceDeploymentService Service { get; set; } = null!;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthStateTask { get; set; }

    private int? _userId;
    private List<SelfServicePackageOption> _packages = [];
    private readonly Dictionary<int, List<SelfServiceComputerOption>> _eligibleComputersByPackageId = [];
    private readonly Dictionary<int, int> _selectedComputerIdByPackageId = [];
    private List<SelfServiceRequestHistoryEntry> _history = [];
    private bool _isLoading = true;
    private int? _requestingPackageId;
    private string? _resultMessage;
    private bool _resultIsError;

    protected override async Task OnInitializedAsync()
    {
        if (AuthStateTask is not null)
        {
            AuthenticationState authState = await AuthStateTask;
            string? userIdClaim = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            _userId = int.TryParse(userIdClaim, out int userId) ? userId : null;
        }

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _isLoading = true;
        try
        {
            if (_userId is not { } userId)
            {
                return;
            }

            _packages = await Service.GetAvailablePackagesAsync(userId);
            _history = await Service.GetRequestHistoryAsync(userId);

            _eligibleComputersByPackageId.Clear();
            foreach (SelfServicePackageOption package in _packages)
            {
                List<SelfServiceComputerOption> computers = await Service.GetEligibleComputersAsync(userId, package.PackageId);
                _eligibleComputersByPackageId[package.PackageId] = computers;
                if (computers.Count > 0 && !_selectedComputerIdByPackageId.ContainsKey(package.PackageId))
                {
                    _selectedComputerIdByPackageId[package.PackageId] = computers[0].ComputerId;
                }
            }
        }
        finally
        {
            _isLoading = false;
        }
    }

    private List<SelfServiceComputerOption> EligibleComputers(int packageId) =>
        _eligibleComputersByPackageId.TryGetValue(packageId, out List<SelfServiceComputerOption>? computers) ? computers : [];

    private async Task RequestAsync(int packageId)
    {
        if (_userId is not { } userId) return;
        if (!_selectedComputerIdByPackageId.TryGetValue(packageId, out int computerId)) return;

        _requestingPackageId = packageId;
        _resultMessage = null;
        try
        {
            DeploymentAssignmentResult result = await Service.RequestDeploymentAsync(userId, packageId, computerId);
            _resultIsError = result.Status != DeploymentAssignmentStatus.Success;
            _resultMessage = result.Status == DeploymentAssignmentStatus.Success
                ? "Demande envoyée : le paquet va être installé sur votre poste."
                : (result.ErrorMessage ?? "La demande a échoué.");
        }
        finally
        {
            _requestingPackageId = null;
        }

        await LoadAsync();
    }
}
