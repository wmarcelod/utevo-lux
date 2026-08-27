using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using OpenTibiaVision.Core;
using OpenTibiaVision.ViewModels;

namespace OpenTibiaVision.Features.Overlays.Notes;

/// <summary>
/// Backs the Notes dashboard: create/remove sticky notes, edit the selected note (text, font,
/// colours, independent opacities), and show/hide/lock them. Notes persist through the shared
/// <see cref="ISettingsStore"/> under one key (atomic + 400 ms debounced).
/// </summary>
public sealed class NotesPageViewModel : ViewModelBase
{
    public const string NotesKey = "overlays.notes";

    private readonly IAppServices _services;
    private NoteRowViewModel? _selected;
    private string _status = "Pronto.";

    public NotesPageViewModel(IAppServices services)
    {
        _services = services;
        AddNoteCommand = new RelayCommand(AddNote);
        ShowAllCommand = new RelayCommand(() => SetAllVisible(true));
        HideAllCommand = new RelayCommand(() => SetAllVisible(false));
    }

    public ObservableCollection<NoteRowViewModel> Notes { get; } = new();

    public NoteRowViewModel? SelectedNote
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public IReadOnlyList<string> SystemFonts { get; } = OverlayUi.SystemFonts();

    public ICommand AddNoteCommand { get; }
    public ICommand ShowAllCommand { get; }
    public ICommand HideAllCommand { get; }

    // ---- create / remove ----

    private void AddNote()
    {
        var config = new NoteConfig
        {
            Text = "Nova nota",
            Left = 320,
            Top = 320,
            Visible = true,
            Locked = false,
        };

        var row = BuildRow(config);
        Notes.Add(row);
        SelectedNote = row;
        row.Show();
        Status = "Nota adicionada.";
        Save();
    }

    private NoteRowViewModel BuildRow(NoteConfig config)
    {
        var row = new NoteRowViewModel(_services, config);
        row.RemoveRequested += OnRowRemoveRequested;
        row.Changed += Save;
        return row;
    }

    private void OnRowRemoveRequested(NoteRowViewModel row)
    {
        row.RemoveRequested -= OnRowRemoveRequested;
        row.Changed -= Save;
        Notes.Remove(row);
        if (ReferenceEquals(SelectedNote, row))
            SelectedNote = Notes.FirstOrDefault();
        Status = "Nota removida.";
        Save();
    }

    // ---- global show/hide (bound to the hotkey) ----

    /// <summary>Toggle every note: if any is visible, hide all; else show all.</summary>
    public void ToggleAllVisible()
    {
        bool anyVisible = Notes.Any(n => n.Visible);
        SetAllVisible(!anyVisible);
        _services.ShowToast(anyVisible ? "Notas ocultadas." : "Notas exibidas.");
    }

    private void SetAllVisible(bool visible)
    {
        foreach (NoteRowViewModel row in Notes)
        {
            if (visible)
                row.Show();
            else
                row.Hide();
        }
    }

    // ---- persistence ----

    public void Save()
        => _services.Settings.Set(NotesKey, Notes.Select(n => n.Config).ToList());

    public async Task RestoreAsync(IProgress<string> progress, CancellationToken ct)
    {
        List<NoteConfig> configs = _services.Settings.Get(NotesKey, new List<NoteConfig>());
        if (configs.Count == 0)
        {
            progress.Report("Nenhuma nota salva.");
            return;
        }

        int shown = 0;
        foreach (NoteConfig config in configs)
        {
            ct.ThrowIfCancellationRequested();
            var row = BuildRow(config);
            Notes.Add(row);

            if (config.Visible)
            {
                progress.Report($"Restaurando nota {++shown}...");
                row.Show();
                await Task.Delay(50, ct); // inter-item stagger (optimization principle 6)
            }
        }

        SelectedNote ??= Notes.FirstOrDefault();
        Status = $"{Notes.Count} notas carregadas.";
        progress.Report(Status);
    }

    /// <summary>App shutdown: close windows without flipping Visible, then flush.</summary>
    public void Shutdown()
    {
        foreach (NoteRowViewModel row in Notes)
            row.CloseWindowKeepState();
        _services.Settings.Flush();
    }
}
