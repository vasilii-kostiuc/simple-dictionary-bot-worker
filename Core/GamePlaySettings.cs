namespace BotWorker.Domain;

public class GamePlaySettings
{
    public int CorrectAnswerProbabilityPercent { get; set; } = 70;
    public int AnswerDelayMinMs { get; set; } = 500;
    public int AnswerDelayMaxMs { get; set; } = 3000;
}
