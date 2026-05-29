namespace BotWorker.Worker;

public class BotWorkerSettings
{
    public string WssUrl { get; set; } = "ws://localhost:8080";
    public string ApiUrl { get; set; } = "http://localhost:8876";
    public int MinBotsInQueue { get; set; } = 2;
    public int MaxBotsInQueue { get; set; } = 5;
    public int QueueTimeoutSeconds { get; set; } = 300;
    public int PoolCheckIntervalSeconds { get; set; } = 5;
    public MatchParamsSettings MatchParams { get; set; } = new();
    public GamePlaySettings GamePlay { get; set; } = new();
}


public class MatchParamsSettings
{
    public int LanguageFromId { get; set; } = 1;
    public int LanguageToId { get; set; } = 2;
    public string MatchType { get; set; } = "steps";

    /// <summary>
    /// For match_type=steps: { "steps": N }
    /// For match_type=time:  { "duration": N } (seconds, 60–3600)
    /// </summary>
    public Dictionary<string, int> MatchTypeParams { get; set; } = new() { ["steps"] = 5 };
}
