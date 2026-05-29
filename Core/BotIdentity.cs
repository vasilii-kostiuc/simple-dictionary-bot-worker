namespace BotWorker.Domain;

public record BotIdentity(Guid GuestId, string Name)
{
    public string ParticipantType => "guest";
    public string ParticipantId => GuestId.ToString();
}
