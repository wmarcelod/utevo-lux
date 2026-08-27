using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using UtevoLux.Core;
using UtevoLux.ViewModels;

namespace UtevoLux.Features.Link;

/// <summary>
/// Backs the Utevo Link page and the click-through overlay: connect + authenticate, create or
/// join a party by code, and track live member presence. Faithful port of the original
/// <c>WindowReplicaApp.ViewModels.LinkViewModel</c> — same commands, properties, events, and the
/// auto-rejoin-with-backoff behaviour on an unexpected drop.
///
/// Fork adaptations (the original depended on a license + HardwareIdService + an app IAudioPlayer,
/// none of which exist here):
///   - auth identity = a stable generated <see cref="LinkSettings.ClientId"/> + a hashed HWID
///     (see <see cref="LinkIdentity"/>);
///   - state persists through the shared <c>ISettingsStore</c> (atomic + debounced) under
///     "link.settings";
///   - the "member disconnected" cue falls back to a system chime (volume-gated) since the Audio
///     module's <c>SoundEngine</c> is not exposed on <c>IAppServices</c>.
/// The relay may well be offline: every path degrades to a status message, never an exception.
/// </summary>
public sealed class LinkViewModel : INotifyPropertyChanged
{
    private const string SettingsKey = "link.settings";

    public static readonly int[] AllowedDurationsMinutes = { 60, 120, 240, 480 };
    public const int DefaultDurationMinutes = 120;

    private static readonly TimeSpan AutoRejoinFirstDelay = TimeSpan.FromSeconds(3.0);
    private static readonly TimeSpan AutoRejoinSecondDelay = TimeSpan.FromSeconds(8.0);

    private readonly LinkClientService _client = new();
    private readonly IAppServices _services;
    private readonly LinkSettings _settings;

    private string? _lastPartyCode;
    private bool _explicitLeave;
    private bool _autoRejoinInProgress;

    private bool _enabled;
    private bool _visible = true;
    private bool _locked = true;
    private double _x;
    private double _y;
    private double _scale = 1.0;
    private double _backgroundOpacity = 0.7;
    private double _disconnectSoundVolume = 1.0;
    private string _displayName = "";
    private string? _partyCode;
    private string _statusMessage = "";

    public ObservableCollection<PartyMember> Members { get; } = new();

    public bool Enabled
    {
        get => _enabled;
        set { if (_enabled != value) { _enabled = value; OnPropertyChanged(); } }
    }

    public bool Visible
    {
        get => _visible;
        set { if (_visible != value) { _visible = value; OnPropertyChanged(); } }
    }

    public bool Locked
    {
        get => _locked;
        set { if (_locked != value) { _locked = value; OnPropertyChanged(); } }
    }

    public double X
    {
        get => _x;
        set { if (_x != value) { _x = value; OnPropertyChanged(); } }
    }

    public double Y
    {
        get => _y;
        set { if (_y != value) { _y = value; OnPropertyChanged(); } }
    }

    public double Scale
    {
        get => _scale;
        set { if (_scale != value) { _scale = value; OnPropertyChanged(); } }
    }

    public double BackgroundOpacity
    {
        get => _backgroundOpacity;
        set { if (_backgroundOpacity != value) { _backgroundOpacity = value; OnPropertyChanged(); } }
    }

    public double DisconnectSoundVolume
    {
        get => _disconnectSoundVolume;
        set { if (_disconnectSoundVolume != value) { _disconnectSoundVolume = value; OnPropertyChanged(); } }
    }

    public string DisplayName
    {
        get => _displayName;
        set { if (_displayName != value) { _displayName = value; OnPropertyChanged(); } }
    }

    public string? PartyCode
    {
        get => _partyCode;
        private set
        {
            if (_partyCode != value)
            {
                _partyCode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsInParty));
            }
        }
    }

    public bool IsInParty => !string.IsNullOrEmpty(PartyCode);

    public string StatusMessage
    {
        get => _statusMessage;
        set { if (_statusMessage != value) { _statusMessage = value; OnPropertyChanged(); } }
    }

    public ICommand CreatePartyCommand { get; }
    public ICommand JoinPartyCommand { get; }
    public ICommand LeavePartyCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? EnabledChanged;
    public event Action? LockedChanged;
    public event Action? SettingsChanged;

    public LinkViewModel(IAppServices services)
    {
        _services = services;
        _settings = LoadSettings();
        ApplySettings(_settings);
        LinkIdentity.EnsureClientId(_settings);
        Persist(); // make sure the generated ClientId is written on first run

        CreatePartyCommand = new RelayCommand(
            async param => await CreatePartyAsync(param is int n ? n : DefaultDurationMinutes),
            _ => !IsInParty);
        JoinPartyCommand = new RelayCommand(
            async code => await JoinPartyAsync(code as string),
            _ => !IsInParty);
        LeavePartyCommand = new RelayCommand(
            async _ => await LeavePartyAsync(),
            _ => IsInParty);

        _client.AuthFailed += msg => StatusMessage = msg ?? "Falha na autenticacao.";
        _client.JoinFailed += msg => StatusMessage = msg ?? "Nao foi possivel entrar na party.";
        _client.PartyCreated += (code, members) => OnPartyEntered(code, members);
        _client.PartyJoined += (code, members) => OnPartyEntered(code, members);

        _client.MemberJoined += member =>
            Application.Current?.Dispatcher.Invoke(() => Members.Add(member));

        _client.MemberLeft += (playerId, _) =>
            Application.Current?.Dispatcher.Invoke(() =>
            {
                for (int i = Members.Count - 1; i >= 0; i--)
                    if (Members[i].PlayerId == playerId)
                        Members.RemoveAt(i);
            });

        _client.MemberStatusChanged += (playerId, status) =>
            Application.Current?.Dispatcher.Invoke(() =>
            {
                foreach (PartyMember member in Members)
                {
                    if (member.PlayerId == playerId)
                    {
                        member.Status = status;
                        if (status == PartyMemberStatus.Disconnected)
                            PlayDisconnectCue();
                        break;
                    }
                }
            });

        _client.Disconnected += () =>
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Members.Clear();
                PartyCode = null;
                NotifySettingsChanged();

                bool explicitLeave = _explicitLeave;
                _explicitLeave = false;
                if (!explicitLeave && !string.IsNullOrEmpty(_lastPartyCode))
                {
                    StatusMessage = "Conexao perdida. Reconectando...";
                    _ = AttemptAutoRejoinAsync(_lastPartyCode!);
                }
                else
                {
                    StatusMessage = "Desconectado do Utevo Link.";
                }
            });

        _client.PartyExpired += () =>
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Members.Clear();
                PartyCode = null;
                _lastPartyCode = null;
                StatusMessage = "Sua party expirou.";
                NotifySettingsChanged();
            });
    }

    // ---- connection / auth ----

    public async Task<bool> ConnectAsync()
    {
        StatusMessage = "";

        string licenseKey = LinkIdentity.EnsureClientId(_settings);
        string hwid = LinkIdentity.GetHardwareId();
        Persist();

        bool ok = await _client.ConnectAndAuthenticateAsync(licenseKey, hwid, DisplayName);
        if (ok && !Enabled)
        {
            Enabled = true;
            OnEnabledChanged();
        }
        return ok;
    }

    public void DisconnectFromServer()
    {
        _client.Disconnect();
        if (Enabled)
        {
            Enabled = false;
            OnEnabledChanged();
        }
    }

    private async Task<bool> EnsureConnectedAsync()
    {
        if (_client.IsConnected && _client.IsAuthenticated)
            return true;
        return await ConnectAsync();
    }

    // ---- party lifecycle ----

    public async Task CreatePartyAsync(int durationMinutes = DefaultDurationMinutes)
    {
        StatusMessage = "";
        if (await EnsureConnectedAsync())
            await _client.CreatePartyAsync(durationMinutes);
    }

    public async Task JoinPartyAsync(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return;

        StatusMessage = "";
        if (await EnsureConnectedAsync())
            await _client.JoinPartyAsync(code);
    }

    public async Task LeavePartyAsync()
    {
        _explicitLeave = true;
        _lastPartyCode = null;
        await _client.LeavePartyAsync();
        Members.Clear();
        PartyCode = null;
        NotifySettingsChanged();
    }

    private void OnPartyEntered(string? code, List<PartyMember> members)
        => Application.Current?.Dispatcher.Invoke(() =>
        {
            Members.Clear();
            foreach (PartyMember member in members)
                Members.Add(member);
            PartyCode = code;
            _lastPartyCode = code;
            StatusMessage = "";
            NotifySettingsChanged();
        });

    private async Task AttemptAutoRejoinAsync(string code)
    {
        if (_autoRejoinInProgress)
            return;

        _autoRejoinInProgress = true;
        try
        {
            foreach (TimeSpan delay in new[] { AutoRejoinFirstDelay, AutoRejoinSecondDelay })
            {
                await Task.Delay(delay);
                if (IsInParty || _lastPartyCode != code)
                    return;

                if (await EnsureConnectedAsync())
                {
                    await _client.JoinPartyAsync(code);
                    if (IsInParty)
                        return;
                }
            }

            if (!IsInParty)
            {
                StatusMessage = "Nao foi possivel reconectar ao Utevo Link. Tente entrar novamente.";
                _lastPartyCode = null;
            }
        }
        finally
        {
            _autoRejoinInProgress = false;
        }
    }

    // ---- disconnect cue (fork fallback) ----

    private void PlayDisconnectCue()
    {
        // The original played a bundled "UserDisconnected" sample through the app IAudioPlayer at
        // DisconnectSoundVolume. That pump is not exposed on IAppServices in this fork, so degrade
        // to the system exclamation chime, gated on volume so 0 stays silent. Never throws.
        if (DisconnectSoundVolume <= 0)
            return;
        try { System.Media.SystemSounds.Exclamation.Play(); }
        catch { /* audio unavailable — ignore */ }
    }

    // ---- events + persistence ----

    public void OnEnabledChanged()
    {
        NotifySettingsChanged();
        EnabledChanged?.Invoke();
    }

    public void OnLockedChanged()
    {
        NotifySettingsChanged();
        LockedChanged?.Invoke();
    }

    public void NotifySettingsChanged()
    {
        Persist();
        SettingsChanged?.Invoke();
    }

    private LinkSettings LoadSettings()
    {
        try { return _services.Settings.Get(SettingsKey, new LinkSettings()) ?? new LinkSettings(); }
        catch { return new LinkSettings(); }
    }

    private void ApplySettings(LinkSettings s)
    {
        _enabled = s.Enabled;
        _visible = s.Visible;
        _locked = s.Locked;
        _x = s.X;
        _y = s.Y;
        _scale = s.Scale > 0 ? s.Scale : 1.0;
        _backgroundOpacity = s.BackgroundOpacity;
        _disconnectSoundVolume = s.DisconnectSoundVolume;
        _displayName = s.DisplayName ?? "";
    }

    private void Persist()
    {
        _settings.Enabled = Enabled;
        _settings.Visible = Visible;
        _settings.Locked = Locked;
        _settings.X = X;
        _settings.Y = Y;
        _settings.Scale = Scale;
        _settings.BackgroundOpacity = BackgroundOpacity;
        _settings.DisconnectSoundVolume = DisconnectSoundVolume;
        _settings.DisplayName = DisplayName;
        try { _services.Settings.Set(SettingsKey, _settings); }
        catch { /* persistence is best-effort */ }
    }

    /// <summary>Close the transport on app shutdown; flush pending settings.</summary>
    public void Shutdown()
    {
        Persist();
        _client.Dispose();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
