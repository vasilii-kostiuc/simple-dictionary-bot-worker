using System.Text.Json;

namespace BotWorker.Worker.Bots;

public interface IBotAgent
{
    BotState State { get; }

    Task RunAsync(CancellationToken ct);
}

/// <summary>
/// Thin orchestrator: connect → auth → queue → receive loop.
/// Match gameplay is delegated to <see cref="MatchPlayer"/>.
/// </summary>
public class BotAgent : IBotAgent
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly BotWorkerSettings _settings;
    private readonly MatchPlayer _matchPlayer;
    private readonly ILogger<BotAgent> _logger;

    private BotIdentity? _identity;

    public BotState State { get; private set; } = BotState.Idle;

    public BotAgent(BotWorkerSettings settings, MatchPlayer matchPlayer, ILogger<BotAgent> logger)
    {
        _settings = settings;
        _matchPlayer = matchPlayer;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        await using var ws = new WsConnection();

        try
        {
            await ConnectAndAuthAsync(ws, ct);
            await JoinQueueAsync(ws, ct);
            await RunReceiveLoopAsync(ws, ct);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Bot {GuestId}] Unexpected error", _identity?.GuestId);
        }
        finally
        {
            State = BotState.Finished;
            await SafeCloseAsync(ws);
            _logger.LogInformation("[Bot {GuestId}] Finished", _identity?.GuestId);
        }
    }

    // -------------------------------------------------------------------------
    // Connection & auth
    // -------------------------------------------------------------------------

    private async Task ConnectAndAuthAsync(WsConnection ws, CancellationToken ct)
    {
        State = BotState.Connecting;

        _logger.LogInformation("[Bot] Connecting to {Uri}", _settings.WssUrl);
        await ws.ConnectAsync(new Uri(_settings.WssUrl), ct);

        var guestId = Guid.NewGuid();
        await ws.SendAsync(BuildMessage("guest_auth", new { guest_id = guestId.ToString() }), ct);

        var response = await WaitForMessageAsync(ws, "guest_auth_success", ct);
        if (response is null)
        {
            throw new InvalidOperationException("Did not receive guest_auth_success");
        }

        _identity = new BotIdentity(guestId, $"Bot-{guestId.ToString()[..8]}");
        State = BotState.Authenticated;
        _logger.LogInformation("[Bot {GuestId}] Authenticated as guest", guestId);
    }

    // -------------------------------------------------------------------------
    // Matchmaking
    // -------------------------------------------------------------------------

    private async Task JoinQueueAsync(WsConnection ws, CancellationToken ct)
    {
        await ws.SendAsync(BuildMessage("subscribe", new { channel = "matchmaking.queue" }), ct);

        await ws.SendAsync(
            BuildMessage("matchmaking.join", new
            {
                match_type = _settings.MatchParams.MatchType,
                language_from_id = _settings.MatchParams.LanguageFromId,
                language_to_id = _settings.MatchParams.LanguageToId,
                match_params = new { },
            }),
            ct
        );

        var response = await WaitForMessageAsync(ws, "matchmaking_join_success", ct);
        if (response is null)
        {
            throw new InvalidOperationException("Did not receive matchmaking_join_success");
        }

        State = BotState.InQueue;
        _logger.LogInformation("[Bot {GuestId}] Joined matchmaking queue", _identity!.GuestId);
    }

    // -------------------------------------------------------------------------
    // Receive loop
    // -------------------------------------------------------------------------

    private async Task RunReceiveLoopAsync(WsConnection ws, CancellationToken ct)
    {
        using var queueTimeoutCts = new CancellationTokenSource(
            TimeSpan.FromSeconds(_settings.QueueTimeoutSeconds)
        );
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, queueTimeoutCts.Token);

        try
        {
            while (!linkedCts.Token.IsCancellationRequested && State != BotState.Finished)
            {
                var msg = await ws.ReceiveAsync(linkedCts.Token);

                if (msg is null)
                {
                    _logger.LogInformation("[Bot {GuestId}] WebSocket closed by server", _identity!.GuestId);
                    break;
                }

                await HandleMessageAsync(ws, msg, ct);
            }
        }
        catch (OperationCanceledException) when (queueTimeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _logger.LogInformation(
                "[Bot {GuestId}] Queue timeout ({Seconds}s), leaving queue",
                _identity!.GuestId, _settings.QueueTimeoutSeconds
            );

            try
            {
                await ws.SendAsync(BuildMessage("matchmaking.leave", new { }), ct);
            }
            catch
            {
                // Best effort
            }
        }
    }

    private async Task HandleMessageAsync(WsConnection ws, WsMessage msg, CancellationToken ct)
    {
        if (msg.Type == "error")
        {
            var errorCode = msg.Data?.TryGetProperty("error", out var errProp) == true
                ? errProp.GetString()
                : "unknown";
            _logger.LogWarning("[Bot {GuestId}] Server error: {Error}", _identity!.GuestId, errorCode);
            return;
        }

        // Delegate match lifecycle messages to MatchPlayer
        var newMatchId = await _matchPlayer.HandleAsync(msg, _identity!, ct);

        if (newMatchId is not null)
        {
            // match_created — subscribe to the match channel and update state
            State = BotState.InMatch;
            await ws.SendAsync(BuildMessage("subscribe", new { channel = $"match.{newMatchId}" }), ct);
        }

        if (_matchPlayer.IsFinished)
        {
            State = BotState.Finished;
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static WsMessage BuildMessage(string type, object data)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        var element = JsonSerializer.Deserialize<JsonElement>(json);
        return new WsMessage(type, element);
    }

    private static async Task<WsMessage?> WaitForMessageAsync(
        WsConnection ws,
        string expectedType,
        CancellationToken ct,
        int maxAttempts = 20
    )
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            var msg = await ws.ReceiveAsync(ct);
            if (msg is null)
            {
                return null;
            }

            if (msg.Type == expectedType)
            {
                return msg;
            }
        }

        return null;
    }

    private static async Task SafeCloseAsync(WsConnection ws)
    {
        try
        {
            await ws.CloseAsync(CancellationToken.None);
        }
        catch
        {
            // Best effort
        }
    }
}
