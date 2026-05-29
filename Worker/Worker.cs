namespace BotWorker.Worker;

public class Worker : BackgroundService
{
    private readonly BotPool _pool;
    private readonly ILogger<Worker> _logger;

    public Worker(BotPool pool, ILogger<Worker> logger)
    {
        _pool = pool;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Bot Worker starting");
        await _pool.RunAsync(stoppingToken);
        _logger.LogInformation("Bot Worker stopped");
    }
}
