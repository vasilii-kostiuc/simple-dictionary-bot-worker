var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<BotWorkerSettings>(
    builder.Configuration.GetSection("BotWorker")
);

builder.Services.AddHttpClient<MatchApiClient>();

builder.Services.AddSingleton<BotPool>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
