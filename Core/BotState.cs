namespace BotWorker.Domain;

public enum BotState
{
    Idle,
    Connecting,
    Authenticated,
    InQueue,
    InMatch,
    Finished,
}
