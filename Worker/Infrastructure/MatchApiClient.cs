using System.Net.Http.Json;
using System.Text.Json;

namespace BotWorker.Worker.Infrastructure;

public class MatchApiClient : IAttemptSubmitter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly HttpClient _http;

    public MatchApiClient(HttpClient http, IOptions<BotWorkerSettings> settings)
    {
        _http = http;
        _http.BaseAddress = new Uri(settings.Value.ApiUrl.TrimEnd('/') + "/");
    }

    /// <summary>
    /// Submit a single attempt for a match step.
    /// </summary>
    public async Task SubmitAttemptAsync(
        int matchId,
        int stepId,
        object attemptData,
        int attemptNumber,
        string participantType,
        string participantId,
        CancellationToken ct
    )
    {
        var body = new
        {
            attempt_data = attemptData,
            attempt_number = attemptNumber,
            participant_type = participantType,
            participant_id = participantId,
        };

        var response = await _http.PostAsJsonAsync(
            $"api/v1/matches/{matchId}/steps/{stepId}/attempts",
            body,
            JsonOptions,
            ct
        );

        response.EnsureSuccessStatusCode();
    }
}
