using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UtevoLux.Features.Link;

/// <summary>
/// The TibiaVision Link transport: a <see cref="ClientWebSocket"/> to the Link relay that speaks a
/// small JSON protocol — auth (licenseKey + hwid + name), create_party(durationMinutes),
/// join_party(code), leave_party — and receives live party pushes (party_created / party_joined /
/// member_joined / member_left / member_status / party_expired). Faithful port of the original
/// <c>WindowReplicaApp.Services.LinkClientService</c>: same URL, same message shapes, same events.
///
/// Robustness (unchanged from the original, and important here since the relay may be offline in
/// the fork): connect failures raise <see cref="AuthFailed"/> instead of throwing; the auth wait is
/// bounded to 10 s; every send/receive is wrapped so a socket fault can never crash the app.
/// </summary>
public sealed class LinkClientService : IDisposable
{
    private const string LinkUrl = "wss://link.tibiavision.com";

    private ClientWebSocket? _socket;
    private CancellationTokenSource? _cts;
    private TaskCompletionSource<bool>? _authTcs;

    public bool IsConnected => _socket is { State: WebSocketState.Open };

    public bool IsAuthenticated { get; private set; }

    public string? PartyCode { get; private set; }

    public event Action? Connected;
    public event Action<string?>? AuthFailed;
    public event Action<string?, List<PartyMember>>? PartyCreated;
    public event Action<string?, List<PartyMember>>? PartyJoined;
    public event Action<string?>? JoinFailed;
    public event Action<PartyMember>? MemberJoined;
    public event Action<string?, string?>? MemberLeft;
    public event Action<string?, PartyMemberStatus>? MemberStatusChanged;
    public event Action? Disconnected;
    public event Action? PartyExpired;

    public async Task<bool> ConnectAndAuthenticateAsync(string licenseKey, string hwid, string displayName)
    {
        Disconnect();
        _socket = new ClientWebSocket();
        _cts = new CancellationTokenSource();
        _authTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await _socket.ConnectAsync(new Uri(LinkUrl), _cts.Token);
        }
        catch
        {
            AuthFailed?.Invoke("Nao foi possivel acessar o TibiaVision Link. Verifique sua conexao e tente novamente.");
            return false;
        }

        Connected?.Invoke();
        _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));

        await SendAsync(new
        {
            type = "auth",
            licenseKey,
            hwid,
            name = displayName
        });

        if (await Task.WhenAny(_authTcs.Task, Task.Delay(TimeSpan.FromSeconds(10.0))) != _authTcs.Task)
        {
            AuthFailed?.Invoke("O TibiaVision Link expirou. Tente novamente.");
            return false;
        }

        return await _authTcs.Task;
    }

    public Task CreatePartyAsync(int durationMinutes)
        => SendAsync(new { type = "create_party", durationMinutes });

    public Task JoinPartyAsync(string? code)
        => SendAsync(new { type = "join_party", code = (code ?? "").Trim().ToUpperInvariant() });

    public Task LeavePartyAsync()
    {
        PartyCode = null;
        return SendAsync(new { type = "leave_party" });
    }

    private async Task SendAsync(object payload)
    {
        if (_socket is not { State: WebSocketState.Open })
            return;

        try
        {
            string s = JsonSerializer.Serialize(payload);
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text,
                endOfMessage: true, _cts?.Token ?? CancellationToken.None);
        }
        catch
        {
            // A transient send fault must never crash the app; the receive loop will surface a drop.
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        byte[] buffer = new byte[8192];
        try
        {
            while (_socket is { State: WebSocketState.Open } && !token.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        RaiseDisconnected();
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                HandleMessage(Encoding.UTF8.GetString(ms.ToArray()));
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown / reconnect.
        }
        catch
        {
            RaiseDisconnected();
        }
    }

    private void HandleMessage(string json)
    {
        JsonElement root;
        try
        {
            root = JsonDocument.Parse(json).RootElement;
        }
        catch
        {
            return;
        }

        if (!root.TryGetProperty("type", out JsonElement typeEl))
            return;

        string? type = typeEl.GetString();
        if (type is null)
            return;

        switch (type)
        {
            case "auth_ok":
                IsAuthenticated = true;
                _authTcs?.TrySetResult(true);
                break;

            case "auth_error":
                IsAuthenticated = false;
                AuthFailed?.Invoke(GetString(root, "message"));
                _authTcs?.TrySetResult(false);
                break;

            case "party_created":
                PartyCode = GetString(root, "code");
                PartyCreated?.Invoke(PartyCode, ParseMembers(root.GetProperty("members")));
                break;

            case "party_joined":
                PartyCode = GetString(root, "code");
                PartyJoined?.Invoke(PartyCode, ParseMembers(root.GetProperty("members")));
                break;

            case "join_error":
                JoinFailed?.Invoke(GetString(root, "message"));
                break;

            case "member_joined":
            {
                JsonElement m = root.GetProperty("member");
                MemberJoined?.Invoke(new PartyMember
                {
                    PlayerId = GetString(m, "playerId"),
                    Name = GetString(m, "name"),
                    Status = ParseStatus(GetString(m, "status"))
                });
                break;
            }

            case "member_left":
                MemberLeft?.Invoke(GetString(root, "playerId"), GetString(root, "reason") ?? "left");
                break;

            case "member_status":
                MemberStatusChanged?.Invoke(GetString(root, "playerId"), ParseStatus(GetString(root, "status")));
                break;

            case "party_expired":
                PartyCode = null;
                PartyExpired?.Invoke();
                break;
        }
    }

    private static string? GetString(JsonElement el, string prop)
        => el.TryGetProperty(prop, out JsonElement value) ? value.GetString() : null;

    private static List<PartyMember> ParseMembers(JsonElement arr)
    {
        var list = new List<PartyMember>();
        foreach (JsonElement item in arr.EnumerateArray())
        {
            list.Add(new PartyMember
            {
                PlayerId = GetString(item, "playerId"),
                Name = GetString(item, "name"),
                Status = ParseStatus(GetString(item, "status"))
            });
        }
        return list;
    }

    private static PartyMemberStatus ParseStatus(string? s) => s switch
    {
        "lagging" => PartyMemberStatus.Lagging,
        "disconnected" => PartyMemberStatus.Disconnected,
        _ => PartyMemberStatus.Connected,
    };

    private void RaiseDisconnected()
    {
        IsAuthenticated = false;
        PartyCode = null;
        _authTcs?.TrySetResult(false);
        Disconnected?.Invoke();
    }

    public void Disconnect()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        try { _socket?.Abort(); } catch { /* ignore */ }
        _socket?.Dispose();
        _socket = null;
        IsAuthenticated = false;
        PartyCode = null;
    }

    public void Dispose() => Disconnect();
}
