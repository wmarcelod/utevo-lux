using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UtevoLux.Features.Link;

/// <summary>
/// One member of a TibiaVision Link party. <see cref="Status"/> raises change notifications for
/// both itself and the derived <see cref="StatusText"/> so the page list and the overlay update
/// the coloured dot + label live when the server pushes a member_status event. Faithful port of
/// the original <c>WindowReplicaApp.Models.PartyMember</c>.
/// </summary>
public sealed class PartyMember : INotifyPropertyChanged
{
    private PartyMemberStatus _status;

    /// <summary>Server-assigned stable id; the join/leave/status key.</summary>
    public string? PlayerId { get; set; }

    /// <summary>The display name the member chose when joining.</summary>
    public string? Name { get; set; }

    public PartyMemberStatus Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    /// <summary>Human-readable status shown beside the member name (pt-BR to match the fork UI).</summary>
    public string StatusText => _status switch
    {
        PartyMemberStatus.Connected => "Conectado",
        PartyMemberStatus.Lagging => "Com lag",
        PartyMemberStatus.Disconnected => "Desconectado",
        _ => "",
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
