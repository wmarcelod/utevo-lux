using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenTibiaVision.UI;

namespace OpenTibiaVision.Features.Link;

/// <summary>
/// The TibiaVision Link page: display-name + create/join-by-code setup, the in-party view (code,
/// copyable, live member list), and the overlay controls (lock, scale, opacity, disconnect-cue
/// volume). Built once and kept alive by the module (visibility-toggled on nav). Faithful port of
/// the original <c>WindowReplicaApp.Views.LinkPageControl</c>, restyled with the fork theme.
/// </summary>
public partial class LinkPage : UserControl
{
    private readonly LinkViewModel _viewModel;

    // Gates the slider ValueChanged handlers so restoring saved values in OnLoaded (and WPF's
    // initial min/max coercion during load) never persists a transient value; only genuine user
    // edits after load write back.
    private bool _ready;

    public LinkPage(LinkViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        MembersList.ItemsSource = _viewModel.Members;
        DisplayNameTextBox.Text = _viewModel.DisplayName ?? "";
        ScaleSlider.Value = _viewModel.Scale > 0 ? _viewModel.Scale : 1.0;
        BackgroundOpacitySlider.Value = _viewModel.BackgroundOpacity;
        DisconnectVolumeSlider.Value = _viewModel.DisconnectSoundVolume;
        UpdateLockIcon(_viewModel.Locked);

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        RefreshPanelState();
        RefreshStatus();
        _ready = true;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(LinkViewModel.IsInParty):
            case nameof(LinkViewModel.PartyCode):
                Dispatcher.Invoke(RefreshPanelState);
                break;
            case nameof(LinkViewModel.StatusMessage):
                Dispatcher.Invoke(RefreshStatus);
                break;
            case nameof(LinkViewModel.Locked):
                Dispatcher.Invoke(() => UpdateLockIcon(_viewModel.Locked));
                break;
        }
    }

    private void RefreshPanelState()
    {
        if (_viewModel.IsInParty)
        {
            SetupPanel.Visibility = Visibility.Collapsed;
            PartyPanel.Visibility = Visibility.Visible;
            PartyCodeText.Text = _viewModel.PartyCode ?? "";
        }
        else
        {
            SetupPanel.Visibility = Visibility.Visible;
            PartyPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void RefreshStatus()
    {
        string msg = _viewModel.StatusMessage;
        StatusText.Text = msg;
        StatusText.Visibility = string.IsNullOrEmpty(msg) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateLockIcon(bool isLocked)
    {
        if (isLocked)
        {
            LockToggleIcon.Data = ThemeAccess.Icon("Icon.Lock");
            LockToggleIcon.Fill = ThemeAccess.Brush("TextPrimaryBrush", "#FFF3F5F9");
            LockToggleText.Text = "Travado";
        }
        else
        {
            LockToggleIcon.Data = ThemeAccess.Icon("Icon.Unlock");
            LockToggleIcon.Fill = ThemeAccess.Brush("DangerBrush", "#FFE5534B");
            LockToggleText.Text = "Destravado";
        }
    }

    private bool CommitDisplayName()
    {
        string name = DisplayNameTextBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
        {
            StatusText.Text = "Informe um nome de exibicao.";
            StatusText.Visibility = Visibility.Visible;
            DisplayNameTextBox.Focus();
            return false;
        }
        _viewModel.DisplayName = name;
        _viewModel.NotifySettingsChanged();
        return true;
    }

    private async void CreatePartyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CommitDisplayName())
            return;

        int duration = LinkViewModel.DefaultDurationMinutes;
        if (DurationComboBox.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Tag as string, out int parsed))
            duration = parsed;

        await _viewModel.CreatePartyAsync(duration);
    }

    private async void JoinPartyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!CommitDisplayName())
            return;

        string? code = CodeTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(code))
        {
            StatusText.Text = "Digite um codigo de party primeiro.";
            StatusText.Visibility = Visibility.Visible;
            return;
        }

        await _viewModel.JoinPartyAsync(code);
    }

    private void CopyCodeButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_viewModel.PartyCode))
            return;
        try { Clipboard.SetText(_viewModel.PartyCode); } catch { /* clipboard busy — ignore */ }
    }

    private async void LeavePartyButton_Click(object sender, RoutedEventArgs e)
        => await _viewModel.LeavePartyAsync();

    private void LockToggle_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Locked = !_viewModel.Locked;
        UpdateLockIcon(_viewModel.Locked);
        _viewModel.OnLockedChanged();
    }

    private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        _viewModel.Scale = e.NewValue;
        _viewModel.NotifySettingsChanged();
    }

    private void BackgroundOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        _viewModel.BackgroundOpacity = e.NewValue;
        _viewModel.NotifySettingsChanged();
    }

    private void DisconnectVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        _viewModel.DisconnectSoundVolume = e.NewValue;
        _viewModel.NotifySettingsChanged();
    }
}
