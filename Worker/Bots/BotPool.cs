namespace BotWorker.Worker.Bots;

/// <summary>
/// Manages the fleet of bot agents, keeping between MinBotsInQueue
/// and MaxBotsInQueue bots in the matchmaking queue at all times.
/// </summary>
public class BotPool
{
    private readonly BotWorkerSettings _settings;
    private readonly MatchApiClient _api;
    private readonly ILogger<BotAgent> _botLogger;
    private readonly ILogger<MatchPlayer> _matchPlayerLogger;
    private readonly ILogger<BotPool> _logger;

    private readonly Lock _lock = new();
    private readonly List<(BotAgent Agent, Task Task)> _agents = [];

    public BotPool(
        IOptions<BotWorkerSettings> settings,
        MatchApiClient api,
        ILogger<BotAgent> botLogger,
        ILogger<MatchPlayer> matchPlayerLogger,
        ILogger<BotPool> logger
    )
    {
        _settings = settings.Value;
        _api = api;
        _botLogger = botLogger;
        _matchPlayerLogger = matchPlayerLogger;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _logger.LogInformation(
            "BotPool starting. Min={Min} Max={Max} bots in queue",
            _settings.MinBotsInQueue,
            _settings.MaxBotsInQueue
        );

        while (!ct.IsCancellationRequested)
        {
            Tick(ct);

            await Task.Delay(
                TimeSpan.FromSeconds(_settings.PoolCheckIntervalSeconds),
                ct
            );
        }

        await DrainAsync();
    }

    // -------------------------------------------------------------------------
    // Internal
    // -------------------------------------------------------------------------

    private void Tick(CancellationToken ct)
    {
        lock (_lock)
        {
            // Remove finished agents
            _agents.RemoveAll(pair => pair.Agent.State == BotState.Finished && pair.Task.IsCompleted);

            int inQueue = _agents.Count(pair => pair.Agent.State == BotState.InQueue);
            int total = _agents.Count;

            _logger.LogDebug("BotPool tick: {Total} total, {InQueue} in queue", total, inQueue);

            int toSpawn = _settings.MinBotsInQueue - inQueue;
            int canSpawn = _settings.MaxBotsInQueue - total;
            int spawn = Math.Min(toSpawn, canSpawn);

            for (int i = 0; i < spawn; i++)
            {
                SpawnBot(ct);
            }
        }
    }

    private void SpawnBot(CancellationToken ct)
    {
        var resolver = new StepAnswerResolver();
        var matchPlayer = new MatchPlayer(_api, resolver, _settings.GamePlay, _matchPlayerLogger);
        var agent = new BotAgent(_settings, matchPlayer, _botLogger);
        var task = Task.Run(() => agent.RunAsync(ct), ct);
        _agents.Add((agent, task));
        _logger.LogInformation("Spawned new bot (total agents: {Count})", _agents.Count);
    }

    private async Task DrainAsync()
    {
        List<Task> tasks;

        lock (_lock)
        {
            tasks = _agents.Select(pair => pair.Task).ToList();
        }

        if (tasks.Count > 0)
        {
            _logger.LogInformation("Draining {Count} bot(s)...", tasks.Count);
            await Task.WhenAll(tasks);
        }
    }
}
