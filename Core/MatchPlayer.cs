using System.Text.Json;
using Microsoft.Extensions.Logging;
using BotWorker.Domain.Contracts;

namespace BotWorker.Domain;

/// <summary>
/// Handles the match lifecycle for a single bot:
/// match_created → match_started → next_step_generated → match_completed.
///
/// Returns IsFinished = true when the match ends.
/// </summary>
public class MatchPlayer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly IAttemptSubmitter _api;
    private readonly StepAnswerResolver _resolver;
    private readonly GamePlaySettings _gamePlay;
    private readonly ILogger<MatchPlayer> _logger;
    private readonly Random _rng;

    private int _matchId;
    private bool _matchStarted;

    public bool IsFinished { get; private set; }

    public MatchPlayer(
        IAttemptSubmitter api,
        StepAnswerResolver resolver,
        GamePlaySettings gamePlay,
        ILogger<MatchPlayer> logger,
        Random? rng = null
    )
    {
        _api = api;
        _resolver = resolver;
        _gamePlay = gamePlay;
        _logger = logger;
        _rng = rng ?? Random.Shared;
    }

    /// <summary>
    /// Handles an incoming WSS message. Returns true if the caller should
    /// subscribe to a new channel (match.{id}) after match_created.
    /// </summary>
    public async Task<int?> HandleAsync(WsMessage msg, BotIdentity identity, CancellationToken ct)
    {
        switch (msg.Type)
        {
            case "match_created":
                return OnMatchCreated(msg, identity);

            case "match_started":
                _matchStarted = true;
                _logger.LogInformation("[Bot {GuestId}] Match {MatchId} started", identity.GuestId, _matchId);
                break;

            case "next_step_generated":
                if (_matchStarted)
                {
                    await OnNextStepGeneratedAsync(msg, identity, ct);
                }
                break;

            case "match_completed":
                _logger.LogInformation("[Bot {GuestId}] Match {MatchId} completed", identity.GuestId, _matchId);
                IsFinished = true;
                break;
        }

        return null;
    }

    // -------------------------------------------------------------------------
    // Handlers
    // -------------------------------------------------------------------------

    /// <summary>Returns the new match id so the caller can subscribe to match.{id}.</summary>
    private int? OnMatchCreated(WsMessage msg, BotIdentity identity)
    {
        if (msg.Data is null)
        {
            return null;
        }

        if (msg.Data.Value.TryGetProperty("id", out var idProp))
        {
            _matchId = idProp.GetInt32();
        }

        _logger.LogInformation("[Bot {GuestId}] Match {MatchId} created", identity.GuestId, _matchId);
        return _matchId;
    }

    private async Task OnNextStepGeneratedAsync(WsMessage msg, BotIdentity identity, CancellationToken ct)
    {
        if (msg.Data is null)
        {
            return;
        }

        var data = msg.Data.Value;

        // Only handle steps assigned to this bot
        if (!data.TryGetProperty("guest_id", out var guestIdProp)
            || guestIdProp.GetString() != identity.GuestId.ToString())
        {
            return;
        }

        if (!data.TryGetProperty("id", out var stepIdProp)
            || !data.TryGetProperty("match_id", out var matchIdProp)
            || !data.TryGetProperty("step_type_id", out var stepTypeIdProp)
            || !data.TryGetProperty("step_data", out var stepDataProp)
            || !data.TryGetProperty("required_answers_count", out var requiredCountProp))
        {
            _logger.LogWarning("[Bot {GuestId}] Received malformed next_step_generated", identity.GuestId);
            return;
        }

        var stepId = stepIdProp.GetInt32();
        var matchId = matchIdProp.GetInt32();
        var stepTypeId = stepTypeIdProp.GetInt32();
        var requiredCount = requiredCountProp.GetInt32();
        var stepData = stepDataProp;

        _logger.LogDebug(
            "[Bot {GuestId}] Handling step {StepId} (type={Type}, required={Required})",
            identity.GuestId, stepId, stepTypeId, requiredCount
        );

        bool correct = _rng.Next(100) < _gamePlay.CorrectAnswerProbabilityPercent;

        for (int attemptNumber = 1; attemptNumber <= requiredCount; attemptNumber++)
        {
            var delay = _rng.Next(_gamePlay.AnswerDelayMinMs, _gamePlay.AnswerDelayMaxMs);
            await Task.Delay(delay, ct);

            var attemptData = _resolver.Resolve(stepTypeId, stepData, correct, attemptNumber - 1);

            try
            {
                await _api.SubmitAttemptAsync(
                    matchId, stepId, attemptData, attemptNumber,
                    identity.ParticipantType, identity.ParticipantId, ct
                );

                _logger.LogDebug(
                    "[Bot {GuestId}] Submitted attempt {N} for step {StepId} (correct={Correct})",
                    identity.GuestId, attemptNumber, stepId, correct
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Bot {GuestId}] Failed to submit attempt for step {StepId}", identity.GuestId, stepId);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    public static WsMessage BuildMessage(string type, object data)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        var element = JsonSerializer.Deserialize<JsonElement>(json);
        return new WsMessage(type, element);
    }
}
