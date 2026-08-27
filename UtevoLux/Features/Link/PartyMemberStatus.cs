namespace UtevoLux.Features.Link;

/// <summary>
/// Live presence of a party member, pushed by the Link server. Drives the coloured status dot in
/// both the page list and the click-through overlay. Faithful port of the original
/// <c>WindowReplicaApp.Models.PartyMemberStatus</c> (ordering preserved for JSON/back-compat).
/// </summary>
public enum PartyMemberStatus
{
    Connected,
    Lagging,
    Disconnected
}
