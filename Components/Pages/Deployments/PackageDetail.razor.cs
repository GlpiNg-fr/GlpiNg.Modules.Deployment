using BlazorBootstrap;
using GlpiNg.Modules.Abstractions.Deployment;
using GlpiNg.Modules.Deployment.Models;
using GlpiNg.Modules.Deployment.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using GlpiNg.Modules.Abstractions.Localization;

namespace GlpiNg.Modules.Deployment.Components.Pages.Deployments;

public partial class PackageDetail : ComponentBase, IAsyncDisposable
{
    private sealed record FicheTab(string Key, string Icon, string Label, int? Count);

    private const string FileInputElementId = "packageFileInput";

    [Parameter]
    public int PackageId { get; set; }

    [Inject]
    private IDbContextFactory<DbContext> DbFactory { get; set; } = null!;

    [Inject]
    private NavigationManager Nav { get; set; } = null!;

    [Inject]
    private ToastService ToastService { get; set; } = null!;

    [Inject]
    private DeploymentPackageFileStorageService FileStorage { get; set; } = null!;

    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    [Inject]
    private IDeploymentTargetDirectory TargetDirectory { get; set; } = null!;

    private DbContext? _db;
    private DeploymentPackage? _package;
    private List<DeployComputerGroup> _computerGroups = [];
    private List<DeploymentPackage> _otherPackages = [];
    private List<DeploymentTargetOption> _entities = [];
    private List<DeploymentTargetOption> _groups = [];
    private List<DeploymentTargetOption> _profiles = [];
    private List<DeploymentTargetOption> _users = [];
    private readonly HashSet<int> _selectedTargetIds = [];
    private List<FicheTab> _tabs = [];
    private string _activeTabKey = "paquet";
    private bool _isSaving;
    private bool _isUploading;
    private int _loadedPackageId;

    private DotNetObjectReference<PackageDetail>? _dotNetRef;
    private int _uploadedCount;
    private int _totalToUpload;
    private string? _currentFileName;
    private double _currentFileProgress;

    private List<DeploymentCheckEntry> _checks = [];
    private List<DeploymentActionEntry> _actions = [];
    private List<DeploymentUserInteractionEntry> _interactions = [];

    private DeploymentCheckEntry _newCheck = new();
    private DeploymentActionEntry _newAction = new();
    private DeploymentActionEntry? _editingActionEntry;
    private DeploymentUserInteractionEntry _newInteraction = new();

    private bool CanAddAction => _newAction.Type switch
    {
        DeploymentActionType.Command => !string.IsNullOrWhiteSpace(_newAction.Command),
        DeploymentActionType.Move or DeploymentActionType.Copy =>
            !string.IsNullOrWhiteSpace(_newAction.From) && !string.IsNullOrWhiteSpace(_newAction.To),
        DeploymentActionType.DeleteDirectory or DeploymentActionType.CreateDirectory => !string.IsNullOrWhiteSpace(_newAction.Path),
        _ => false
    };

    protected override async Task OnParametersSetAsync()
    {
        if (_package is not null && _loadedPackageId == PackageId)
        {
            return;
        }

        _loadedPackageId = PackageId;
        _activeTabKey = "paquet";
        _editingActionEntry = null;
        _newAction = new DeploymentActionEntry();

        if (_db is not null)
        {
            await _db.DisposeAsync();
        }

        _db = await DbFactory.CreateDbContextAsync();

        _package = await _db.Set<DeploymentPackage>()
            .Include(package => package.Files).ThenInclude(file => file.Parts)
            .Include(package => package.DeployComputerGroup)
            .Include(package => package.Targets)
            .Include(package => package.SupersededByPackage)
            .FirstOrDefaultAsync(package => package.Id == PackageId);

        if (_package is null)
        {
            return;
        }

        _computerGroups = await _db.Set<DeployComputerGroup>().AsNoTracking().OrderBy(group => group.Name).ToListAsync();
        _otherPackages = await _db.Set<DeploymentPackage>()
            .AsNoTracking()
            .Where(package => package.Id != PackageId)
            .OrderBy(package => package.Name)
            .ToListAsync();

        _entities = (await TargetDirectory.GetEntitiesAsync()).ToList();
        _groups = (await TargetDirectory.GetGroupsAsync()).ToList();
        _profiles = (await TargetDirectory.GetProfilesAsync()).ToList();
        _users = (await TargetDirectory.GetUsersAsync()).ToList();
        _selectedTargetIds.Clear();

        ReloadEntries();
        RebuildTabs();
    }

    private void ReloadEntries()
    {
        if (_package is null)
        {
            return;
        }

        _checks = DeploymentPackageJsonConverter.ParseChecks(_package.ChecksJson);
        _actions = DeploymentPackageJsonConverter.ParseActions(_package.ActionsJson);
        _interactions = DeploymentPackageJsonConverter.ParseUserInteractions(_package.UserInteractionsJson);
    }

    private void RebuildTabs()
    {
        if (_package is null)
        {
            return;
        }

        _tabs =
        [
            new("paquet", "ti-package", "Paquet", null),
            new("content", "ti-rocket", "Actions sur le paquet",
                _checks.Count + _package.Files.Count + _actions.Count + _interactions.Count),
        ];

        // Onglet visible seulement quand le self-service est activé (groupe renseigné), comme
        // sur l'instance GLPI-Inventory de référence (front/deploypackage.form.php) : le paquet
        // #10, dont plugin_glpiinventory_deploygroups_id est renseigné, affiche un 3e onglet
        // "Cibles pour le déploiement à la demande" absent du paquet #9 qui n'a pas de groupe.
        if (_package.DeployComputerGroupId is not null)
        {
            _tabs.Add(new("cibles", "ti-target-arrow", "Cibles pour le déploiement à la demande", _package.Targets.Count));
        }
        else if (_activeTabKey == "cibles")
        {
            _activeTabKey = "paquet";
        }
    }

    private void SetTab(string key)
    {
        _activeTabKey = key;
    }

    private string SupersededByPackageName =>
        _otherPackages.FirstOrDefault(package => package.Id == _package?.SupersededByPackageId)?.Name
        ?? _package?.SupersededByPackage?.Name
        ?? "—";

    // --- Cibles pour le déploiement à la demande (self-service) ---

    private async Task AddTargetAsync(ChangeEventArgs e)
    {
        if (_db is null || _package is null)
        {
            return;
        }

        string? value = e.Value?.ToString();
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        string[] parts = value.Split(':', 2);
        if (parts.Length != 2 || !int.TryParse(parts[1], out int itemId))
        {
            return;
        }

        DeploymentPackageTargetType? type = parts[0] switch
        {
            "entity" => DeploymentPackageTargetType.Entity,
            "group" => DeploymentPackageTargetType.Group,
            "profile" => DeploymentPackageTargetType.Profile,
            "user" => DeploymentPackageTargetType.User,
            _ => null
        };

        if (type is null || _package.Targets.Any(t => t.Type == type && t.ItemId == itemId))
        {
            return;
        }

        _db.Set<DeploymentPackageTarget>().Add(new DeploymentPackageTarget
        {
            DeploymentPackageId = _package.Id,
            Type = type.Value,
            ItemId = itemId
        });
        await _db.SaveChangesAsync();
        await ReloadTargetsAsync();
        RebuildTabs();
    }

    private async Task ReloadTargetsAsync()
    {
        if (_db is null || _package is null)
        {
            return;
        }

        _package.Targets = await _db.Set<DeploymentPackageTarget>()
            .AsNoTracking()
            .Where(target => target.DeploymentPackageId == _package.Id)
            .OrderBy(target => target.Id)
            .ToListAsync();
    }

    private bool AllTargetsSelected => _package is not null && _package.Targets.Count > 0 && _selectedTargetIds.Count == _package.Targets.Count;

    private void ToggleSelectAllTargets(bool selectAll)
    {
        _selectedTargetIds.Clear();
        if (selectAll && _package is not null)
        {
            foreach (DeploymentPackageTarget target in _package.Targets)
            {
                _selectedTargetIds.Add(target.Id);
            }
        }
    }

    private void ToggleSelectTarget(int targetId, bool selected)
    {
        if (selected)
        {
            _selectedTargetIds.Add(targetId);
        }
        else
        {
            _selectedTargetIds.Remove(targetId);
        }
    }

    private async Task DeleteSelectedTargetsAsync()
    {
        if (_db is null || _package is null || _selectedTargetIds.Count == 0)
        {
            return;
        }

        await _db.Set<DeploymentPackageTarget>()
            .Where(target => _selectedTargetIds.Contains(target.Id))
            .ExecuteDeleteAsync();
        await ReloadTargetsAsync();
        _selectedTargetIds.Clear();
        RebuildTabs();
    }

    private static string TargetTypeLabel(DeploymentPackageTargetType type) => type switch
    {
        DeploymentPackageTargetType.Entity => Tr.T("Entité"),
        DeploymentPackageTargetType.Group => Tr.T("Groupe"),
        DeploymentPackageTargetType.Profile => Tr.T("Profil"),
        DeploymentPackageTargetType.User => Tr.T("Utilisateur"),
        _ => type.ToString()
    };

    private string TargetRecipientLabel(DeploymentPackageTarget target)
    {
        List<DeploymentTargetOption> options = target.Type switch
        {
            DeploymentPackageTargetType.Entity => _entities,
            DeploymentPackageTargetType.Group => _groups,
            DeploymentPackageTargetType.Profile => _profiles,
            DeploymentPackageTargetType.User => _users,
            _ => []
        };

        return options.FirstOrDefault(option => option.Id == target.ItemId)?.Name ?? "—";
    }

    private async Task SaveAsync()
    {
        if (_db is null || _package is null)
        {
            return;
        }

        _isSaving = true;
        try
        {
            await _db.SaveChangesAsync();
            ToastService.Notify(new ToastMessage(ToastType.Success, Tr.T("Paquet enregistré.")));
        }
        finally
        {
            _isSaving = false;
        }
    }

    private async Task DeleteAsync()
    {
        if (_db is null || _package is null)
        {
            return;
        }

        List<string> storagePaths = _package.Files.SelectMany(file => file.Parts)
            .Select(part => part.StoragePath)
            .ToList();

        _db.Set<DeploymentPackage>().Remove(_package);
        await _db.SaveChangesAsync();

        await FileStorage.DeleteOrphanedPartsAsync(_db, storagePaths);

        Nav.NavigateTo("/tools/deployments");
    }

    // --- Vérifications (audits) ---

    private async Task AddCheckAsync()
    {
        if (_package is null || string.IsNullOrWhiteSpace(_newCheck.Path))
        {
            return;
        }

        _checks.Add(_newCheck);
        _newCheck = new DeploymentCheckEntry();
        await PersistEntriesAsync();
    }

    private async Task RemoveCheckAsync(DeploymentCheckEntry entry)
    {
        _checks.Remove(entry);
        await PersistEntriesAsync();
    }

    // --- Actions ---

    /// <summary>Charge <paramref name="entry"/> (une copie, pas la référence) dans le formulaire
    /// d'ajout pour l'éditer ; <see cref="SaveActionAsync"/> la réinjecte ensuite à sa place dans
    /// <see cref="_actions"/> plutôt que de l'ajouter en fin de liste.</summary>
    private void EditAction(DeploymentActionEntry entry)
    {
        _editingActionEntry = entry;
        _newAction = new DeploymentActionEntry
        {
            Type = entry.Type,
            Label = entry.Label,
            Command = entry.Command,
            LogLineLimit = entry.LogLineLimit,
            ExpectedReturnCode = entry.ExpectedReturnCode,
            From = entry.From,
            To = entry.To,
            Path = entry.Path
        };
    }

    private void CancelEditAction()
    {
        _editingActionEntry = null;
        _newAction = new DeploymentActionEntry();
    }

    private async Task SaveActionAsync()
    {
        if (_package is null || !CanAddAction)
        {
            return;
        }

        if (_editingActionEntry is not null)
        {
            int index = _actions.IndexOf(_editingActionEntry);
            if (index >= 0)
            {
                _actions[index] = _newAction;
            }

            _editingActionEntry = null;
        }
        else
        {
            _actions.Add(_newAction);
        }

        _newAction = new DeploymentActionEntry();
        await PersistEntriesAsync();
    }

    private async Task RemoveActionAsync(DeploymentActionEntry entry)
    {
        if (ReferenceEquals(entry, _editingActionEntry))
        {
            CancelEditAction();
        }

        _actions.Remove(entry);
        await PersistEntriesAsync();
    }

    /// <summary>Réordonne une action de <paramref name="offset"/> position(s) (-1 monte, +1 descend).
    /// L'ordre de la liste est celui dans lequel <see cref="DeployJobJsonBuilder"/> sérialise
    /// "actions" pour l'agent, donc réordonner ici change l'ordre d'exécution sur le poste.</summary>
    private async Task MoveActionAsync(DeploymentActionEntry entry, int offset)
    {
        int index = _actions.IndexOf(entry);
        int newIndex = index + offset;
        if (index < 0 || newIndex < 0 || newIndex >= _actions.Count)
        {
            return;
        }

        (_actions[index], _actions[newIndex]) = (_actions[newIndex], _actions[index]);
        await PersistEntriesAsync();
    }

    // --- Interactions utilisateur ---

    private async Task AddInteractionAsync()
    {
        if (_package is null || string.IsNullOrWhiteSpace(_newInteraction.Text))
        {
            return;
        }

        _interactions.Add(_newInteraction);
        _newInteraction = new DeploymentUserInteractionEntry();
        await PersistEntriesAsync();
    }

    private async Task RemoveInteractionAsync(DeploymentUserInteractionEntry entry)
    {
        _interactions.Remove(entry);
        await PersistEntriesAsync();
    }

    private async Task PersistEntriesAsync()
    {
        if (_db is null || _package is null)
        {
            return;
        }

        _package.ChecksJson = DeploymentPackageJsonConverter.SerializeChecks(_checks);
        _package.ActionsJson = DeploymentPackageJsonConverter.SerializeActions(_actions);
        _package.UserInteractionsJson = DeploymentPackageJsonConverter.SerializeUserInteractions(_interactions);

        await _db.SaveChangesAsync();
        RebuildTabs();
    }

    // --- Fichiers ---

    /// <summary>
    /// Lance l'envoi des fichiers sélectionnés dans l'input HTML #packageFileInput via
    /// glping.uploadPackageFiles (glping.js) : une requête HTTP multipart classique par fichier
    /// vers DeploymentPackageFilesController, plutôt que via le composant Blazor InputFile — voir
    /// la remarque dans DeploymentPackageFileStorageService sur la fiabilité de InputFile/SignalR
    /// pour les gros fichiers (paquets de plusieurs Go).
    /// </summary>
    private async Task StartUploadAsync()
    {
        if (_db is null || _package is null || _isUploading)
        {
            return;
        }

        _isUploading = true;
        _currentFileName = null;
        _currentFileProgress = 0;
        _uploadedCount = 0;
        _totalToUpload = 0;
        StateHasChanged();

        try
        {
            _dotNetRef ??= DotNetObjectReference.Create(this);
            string uploadUrl = $"/deployment-packages/{_package.Id}/files";
            int uploadedCount = await JS.InvokeAsync<int>("glping.uploadPackageFiles", FileInputElementId, uploadUrl, _dotNetRef);

            if (uploadedCount > 0)
            {
                // .Query().Include(...) plutôt que .LoadAsync() seul : un fichier tout juste
                // uploadé est nouveau pour le change tracker, donc sa collection Parts ne serait
                // pas chargée sans cet Include explicite — nécessaire pour pouvoir le supprimer
                // ensuite (RemoveFileAsync a besoin de Parts pour effacer les fragments sur disque).
                await _db.Entry(_package).Collection(p => p.Files).Query().Include(f => f.Parts).LoadAsync();
                RebuildTabs();
            }
        }
        catch (JSException ex)
        {
            ToastService.Notify(new ToastMessage(ToastType.Danger, Tr.T("Échec de l'envoi du fichier : {0}", ex.Message)));
        }
        finally
        {
            _isUploading = false;
            _currentFileName = null;
            _currentFileProgress = 0;
            StateHasChanged();
        }
    }

    /// <summary>Rappelée depuis glping.uploadPackageFiles à chaque tick de progression
    /// (XMLHttpRequest.upload.onprogress) de l'upload HTTP en cours, un fichier à la fois.</summary>
    [JSInvokable]
    public Task OnUploadProgress(string fileName, int index, int total, double percent)
    {
        _currentFileName = fileName;
        _uploadedCount = index;
        _totalToUpload = total;
        _currentFileProgress = percent;
        return InvokeAsync(StateHasChanged);
    }

    private async Task RemoveFileAsync(DeploymentPackageFile file)
    {
        if (_db is null || _package is null)
        {
            return;
        }

        List<string> storagePaths = file.Parts.Select(part => part.StoragePath).ToList();

        _db.Set<DeploymentPackageFile>().Remove(file);
        await _db.SaveChangesAsync();

        await FileStorage.DeleteOrphanedPartsAsync(_db, storagePaths);

        await _db.Entry(_package).Collection(p => p.Files).Query().Include(f => f.Parts).LoadAsync();
        RebuildTabs();
    }

    private static string CheckTypeLabel(DeploymentCheckType type) => type switch
    {
        DeploymentCheckType.RegistryKeyExists => Tr.T("La clef de registre existe"),
        DeploymentCheckType.RegistryValueExists => Tr.T("La valeur de la clef existe"),
        DeploymentCheckType.RegistryKeyNotExists => Tr.T("La clef de registre n'existe pas"),
        DeploymentCheckType.RegistryValueNotExists => Tr.T("La valeur de la clef n'existe pas"),
        DeploymentCheckType.RegistryValueEquals => Tr.T("La valeur de la clef est égale à"),
        DeploymentCheckType.RegistryValueNotEquals => Tr.T("La valeur de la clef n'est pas égale à"),
        DeploymentCheckType.FileExists => Tr.T("Le fichier existe"),
        DeploymentCheckType.FileNotExists => Tr.T("Le fichier n'existe pas"),
        DeploymentCheckType.FileSizeGreater => Tr.T("Taille du fichier supérieure à"),
        DeploymentCheckType.FileSizeEquals => Tr.T("Taille du fichier égale à"),
        DeploymentCheckType.FileSizeLower => Tr.T("Taille du fichier inférieure à"),
        DeploymentCheckType.FileSha512Equals => Tr.T("La valeur du hash SHA-512 correspond à"),
        DeploymentCheckType.FileSha512NotEquals => Tr.T("La valeur du hash SHA-512 ne correspond pas à"),
        DeploymentCheckType.DirectoryExists => Tr.T("Le répertoire existe"),
        DeploymentCheckType.DirectoryNotExists => Tr.T("Le répertoire n'existe pas"),
        DeploymentCheckType.FreeSpaceGreater => Tr.T("L'espace libre est supérieur à"),
        _ => type.ToString()
    };

    private static string ActionTypeLabel(DeploymentActionType type) => type switch
    {
        DeploymentActionType.Command => Tr.T("Commande"),
        DeploymentActionType.Move => Tr.T("Déplacer"),
        DeploymentActionType.Copy => Tr.T("Copier"),
        DeploymentActionType.DeleteDirectory => Tr.T("Supprimer un répertoire"),
        DeploymentActionType.CreateDirectory => Tr.T("Créer un répertoire"),
        _ => type.ToString()
    };

    private static string InteractionTypeLabel(DeploymentUserInteractionType type) => type switch
    {
        DeploymentUserInteractionType.InfoMessage => Tr.T("Message d'information"),
        DeploymentUserInteractionType.AcceptRefuse => Tr.T("Accepter / Refuser"),
        _ => type.ToString()
    };

    private static string ActionSummary(DeploymentActionEntry entry) => entry.Type switch
    {
        DeploymentActionType.Command => entry.Command ?? "—",
        DeploymentActionType.Move or DeploymentActionType.Copy => $"{entry.From} → {entry.To}",
        DeploymentActionType.DeleteDirectory or DeploymentActionType.CreateDirectory => entry.Path ?? "—",
        _ => "—"
    };

    private static string FormatSize(long bytes)
    {
        string[] units = ["o", "Ko", "Mo", "Go"];
        double size = bytes;
        int unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.##} {units[unitIndex]}";
    }

    public async ValueTask DisposeAsync()
    {
        _dotNetRef?.Dispose();

        if (_db is not null)
        {
            await _db.DisposeAsync();
        }
    }
}
