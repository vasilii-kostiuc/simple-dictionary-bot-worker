namespace BotWorker.Domain.Contracts;

public interface IAttemptSubmitter
{
    Task SubmitAttemptAsync(
        int matchId,
        int stepId,
        object attemptData,
        int attemptNumber,
        string participantType,
        string participantId,
        CancellationToken ct
    );
}
