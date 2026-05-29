using System.Text.Json;

namespace BotWorker.Domain;

/// <summary>
/// Pure domain logic: builds the attempt_data payload for a given step type.
/// No external dependencies — fully unit-testable.
///
/// Step types:
///   1 - ChooseCorrectAnswer  — step_data: { word_id, answers[{word_id}] }
///   2 - WriteCorrectAnswer   — step_data: { acceptable_answers[] }
///   3 - EstablishCompliance  — step_data: { answers_order[] }
/// </summary>
public class StepAnswerResolver
{
    private readonly Random _rng;

    public StepAnswerResolver(Random? rng = null)
    {
        _rng = rng ?? Random.Shared;
    }

    public object Resolve(int stepTypeId, JsonElement stepData, bool correct, int subIndex = 0)
    {
        return stepTypeId switch
        {
            1 => ResolveChooseCorrectAnswer(stepData, correct),
            2 => ResolveWriteCorrectAnswer(stepData, correct),
            3 => ResolveEstablishCompliance(stepData, correct, subIndex),
            _ => ResolveChooseCorrectAnswer(stepData, correct),
        };
    }

    private object ResolveChooseCorrectAnswer(JsonElement stepData, bool correct)
    {
        if (!stepData.TryGetProperty("word_id", out var wordIdProp))
        {
            return new { word_id = 0 };
        }

        int correctWordId = wordIdProp.GetInt32();

        if (correct)
        {
            return new { word_id = correctWordId };
        }

        if (stepData.TryGetProperty("answers", out var answersProp))
        {
            var wrongIds = answersProp
                .EnumerateArray()
                .Where(a => a.TryGetProperty("word_id", out var wid) && wid.GetInt32() != correctWordId)
                .Select(a => a.GetProperty("word_id").GetInt32())
                .ToList();

            if (wrongIds.Count > 0)
            {
                return new { word_id = wrongIds[_rng.Next(wrongIds.Count)] };
            }
        }

        return new { word_id = correctWordId };
    }

    private object ResolveWriteCorrectAnswer(JsonElement stepData, bool correct)
    {
        if (correct && stepData.TryGetProperty("acceptable_answers", out var acceptable))
        {
            var answers = acceptable.EnumerateArray().Select(a => a.GetString()).ToList();
            if (answers.Count > 0 && answers[0] is not null)
            {
                return new { word = answers[0]! };
            }
        }

        return new { word = "__wrong__" };
    }

    private object ResolveEstablishCompliance(JsonElement stepData, bool correct, int subIndex)
    {
        if (!stepData.TryGetProperty("answers_order", out var orderProp))
        {
            return new { word_id = 0, answer_id = 0 };
        }

        var order = orderProp.EnumerateArray().Select(e => e.GetInt32()).ToList();

        if (order.Count == 0)
        {
            return new { word_id = 0, answer_id = 0 };
        }

        int idx = Math.Clamp(subIndex, 0, order.Count - 1);
        int wordId = order[idx];

        if (correct)
        {
            return new { word_id = wordId, answer_id = wordId };
        }

        var wrongIds = order.Where(id => id != wordId).ToList();
        int wrongAnswerId = wrongIds.Count > 0
            ? wrongIds[_rng.Next(wrongIds.Count)]
            : wordId + 1;

        return new { word_id = wordId, answer_id = wrongAnswerId };
    }
}
