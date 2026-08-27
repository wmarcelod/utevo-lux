using System;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows.Input;
using Microsoft.Win32;
using UtevoLux.Core;
using UtevoLux.ViewModels;

namespace UtevoLux.Features.Profiles;

/// <summary>
/// Backs the Profiles page: lists every named profile, marks the active one, and drives
/// Create / Switch / Rename / Delete plus import/export of a portable <c>.tvprofile</c> bundle.
/// This is the fork's reimplementation of the original <c>ProfilesViewModel</c>, rewired to the
/// foundation <see cref="IProfileService"/> (where the profile NAME is its id) instead of the
/// original's file-path + <c>MirrorSettingsCollection</c> model. Row action commands take the row
/// as their parameter and fall back to <see cref="SelectedProfile"/> — the original's
/// command-with-target shape.
/// </summary>
public sealed class ProfilesPageViewModel : ViewModelBase
{
    private const string ExportExtension = ".tvprofile";
    private const string ProfileEntryName = "profile.json";

    private readonly IAppServices _services;

    private ProfileRowViewModel? _selectedProfile;
    private string _status = "Pronto.";

    public ProfilesPageViewModel(IAppServices services)
    {
        _services = services;

        NewCommand = new RelayCommand(_ => ExecuteNew());
        SwitchCommand = new RelayCommand(p => ExecuteSwitch(ResolveTarget(p)), p => ResolveTarget(p) is not null);
        RenameCommand = new RelayCommand(p => ExecuteRename(ResolveTarget(p)), p => ResolveTarget(p) is not null);
        DeleteCommand = new RelayCommand(p => ExecuteDelete(ResolveTarget(p)), p => ResolveTarget(p) is not null);
        ImportCommand = new RelayCommand(_ => ExecuteImport());
        ExportCommand = new RelayCommand(p => ExecuteExport(ResolveTarget(p)), p => ResolveTarget(p) is not null);

        Profiles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoProfiles));

        _services.Profiles.ActiveProfileChanged += RefreshList;
        RefreshList();
    }

    public ObservableCollection<ProfileRowViewModel> Profiles { get; } = new();

    public bool HasNoProfiles => Profiles.Count == 0;

    public ProfileRowViewModel? SelectedProfile
    {
        get => _selectedProfile;
        set => SetProperty(ref _selectedProfile, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public ICommand NewCommand { get; }
    public ICommand SwitchCommand { get; }
    public ICommand RenameCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand ExportCommand { get; }

    private System.Windows.Window? Owner => _services.ShellWindow;

    // The WPF ShowDialog(Window) overload throws on a null owner; fall back to the ownerless
    // overload when the shell window isn't available yet.
    private bool ShowDialog(FileDialog dialog)
        => (Owner is null ? dialog.ShowDialog() : dialog.ShowDialog(Owner)) == true;

    private ProfileRowViewModel? ResolveTarget(object? parameter)
        => parameter as ProfileRowViewModel ?? SelectedProfile;

    // ---- list ----

    /// <summary>
    /// Rebuild the list from the service: active profile first, then the rest alphabetically
    /// (the original's ordering). Reselects the active row.
    /// </summary>
    public void RefreshList()
    {
        string active = _services.Profiles.ActiveProfile;

        var ordered = _services.Profiles.Profiles
            .OrderByDescending(n => string.Equals(n, active, StringComparison.OrdinalIgnoreCase))
            .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Profiles.Clear();
        ProfileRowViewModel? activeRow = null;
        foreach (string name in ordered)
        {
            bool isActive = string.Equals(name, active, StringComparison.OrdinalIgnoreCase);
            var row = new ProfileRowViewModel(name, isActive);
            Profiles.Add(row);
            if (isActive)
                activeRow = row;
        }

        if (activeRow is not null)
            SelectedProfile = activeRow;
        else if (!Profiles.Contains(SelectedProfile!))
            SelectedProfile = Profiles.FirstOrDefault();
    }

    // ---- create / switch ----

    private void ExecuteNew()
    {
        string name = NextNewProfileName();
        _services.Profiles.Create(name);
        _services.Profiles.Switch(name); // raises ActiveProfileChanged -> RefreshList
        RefreshList();
        _services.ShowToast($"Perfil ativo: {name}");
        Status = $"Perfil criado: {name}.";
    }

    private void ExecuteSwitch(ProfileRowViewModel? target)
    {
        if (target is null)
            return;
        if (target.IsActive)
        {
            Status = $"'{target.Name}' ja e o perfil ativo.";
            return;
        }

        _services.Profiles.Switch(target.Name); // raises ActiveProfileChanged -> RefreshList
        RefreshList();
        _services.ShowToast($"Perfil ativo: {target.Name}");
        Status = $"Perfil ativado: {target.Name}.";
    }

    // ---- rename ----

    private void ExecuteRename(ProfileRowViewModel? target)
    {
        if (target is null)
            return;

        string? newName = ProfileNameDialog.Prompt(Owner, "Renomear perfil", "Novo nome do perfil:", target.Name);
        if (string.IsNullOrWhiteSpace(newName))
            return;

        newName = newName.Trim();
        if (string.Equals(newName, target.Name, StringComparison.Ordinal))
            return;

        if (ProfileExists(newName))
        {
            _services.Info("Renomear perfil", $"Ja existe um perfil chamado '{newName}'.");
            return;
        }

        _services.Profiles.Rename(target.Name, newName);
        RefreshList();

        ProfileRowViewModel? renamed = Profiles.FirstOrDefault(
            p => string.Equals(p.Name, newName, StringComparison.OrdinalIgnoreCase));
        if (renamed is not null)
            SelectedProfile = renamed;

        Status = $"Perfil renomeado para '{newName}'.";
    }

    // ---- delete ----

    private void ExecuteDelete(ProfileRowViewModel? target)
    {
        if (target is null)
            return;

        // The foundation service refuses to delete the active profile; surface that rather than
        // silently no-op'ing.
        if (target.IsActive)
        {
            _services.Info("Excluir perfil",
                "Nao e possivel excluir o perfil ativo. Troque para outro perfil primeiro.");
            return;
        }

        if (!_services.Confirm("Excluir perfil", $"Excluir o perfil '{target.Name}'?"))
            return;

        _services.Profiles.Delete(target.Name);
        RefreshList();
        Status = $"Perfil excluido: {target.Name}.";
    }

    // ---- import ----

    private void ExecuteImport()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Importar perfil",
            Filter = "Perfil Utevo Lux (*.tvprofile)|*.tvprofile|Perfil JSON (legado, *.json)|*.json",
            Multiselect = false
        };
        if (!ShowDialog(dialog))
            return;

        try
        {
            string source = dialog.FileName;
            string baseName = Sanitize(Path.GetFileNameWithoutExtension(source));
            if (string.IsNullOrWhiteSpace(baseName))
                baseName = "Perfil importado";

            string name = UniqueName(baseName);
            string dest = Path.Combine(ProfilesDir, name + ".json");

            if (Path.GetExtension(source).Equals(ExportExtension, StringComparison.OrdinalIgnoreCase))
            {
                using ZipArchive archive = ZipFile.OpenRead(source);
                ZipArchiveEntry entry = archive.GetEntry(ProfileEntryName)
                    ?? throw new InvalidDataException($"Arquivo de perfil invalido - {ProfileEntryName} ausente.");
                entry.ExtractToFile(dest, overwrite: false);
            }
            else
            {
                File.Copy(source, dest, overwrite: false);
            }

            _services.Profiles.Switch(name); // raises ActiveProfileChanged -> RefreshList
            RefreshList();
            _services.ShowToast($"Perfil ativo: {name}");
            Status = $"Perfil importado: {name}.";
        }
        catch (Exception ex)
        {
            _services.Info("Erro ao importar", $"Falha ao importar o perfil: {ex.Message}");
            Status = "Falha ao importar perfil.";
        }
    }

    // ---- export ----

    private void ExecuteExport(ProfileRowViewModel? target)
    {
        if (target is null)
        {
            _services.Info("Exportar perfil", "Selecione um perfil para exportar.");
            return;
        }

        // Make sure the active profile's pending writes are on disk before we zip it.
        if (target.IsActive)
        {
            try { _services.Profiles.Current.Flush(); } catch { /* best effort */ }
        }

        string sourcePath = Path.Combine(ProfilesDir, target.Name + ".json");
        if (!File.Exists(sourcePath))
        {
            _services.Info("Exportar perfil",
                $"O perfil '{target.Name}' ainda nao foi salvo em disco, entao nao ha nada para exportar.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Exportar perfil",
            Filter = "Perfil Utevo Lux (*.tvprofile)|*.tvprofile",
            FileName = target.Name + ExportExtension
        };
        if (!ShowDialog(dialog))
            return;

        try
        {
            if (File.Exists(dialog.FileName))
                File.Delete(dialog.FileName);

            using (ZipArchive archive = ZipFile.Open(dialog.FileName, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(sourcePath, ProfileEntryName);
            }

            Status = $"Perfil exportado: {target.Name}.";
            _services.ShowToast($"Perfil exportado: {target.Name}");
        }
        catch (Exception ex)
        {
            _services.Info("Erro ao exportar", $"Falha ao exportar o perfil: {ex.Message}");
            Status = "Falha ao exportar perfil.";
        }
    }

    // ---- helpers ----

    private string ProfilesDir => _services.Profiles.Current.RootDirectory;

    private bool ProfileExists(string name)
    {
        if (_services.Profiles.Profiles.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
            return true;
        return File.Exists(Path.Combine(ProfilesDir, name + ".json"));
    }

    private string NextNewProfileName()
    {
        int n = 1;
        string name;
        do { name = $"Perfil {n++}"; }
        while (ProfileExists(name));
        return name;
    }

    private string UniqueName(string baseName)
    {
        if (!ProfileExists(baseName))
            return baseName;

        int n = 1;
        string candidate;
        do { candidate = $"{baseName} ({n++})"; }
        while (ProfileExists(candidate));
        return candidate;
    }

    private static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim();
    }
}
