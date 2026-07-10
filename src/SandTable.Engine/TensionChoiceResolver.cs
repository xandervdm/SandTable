namespace SandTable.Engine;

public sealed class TensionChoiceValidationException(string message) : InvalidOperationException(message);

public sealed record TensionChoiceResult(
    GameState State,
    TensionDecision Decision,
    IReadOnlyList<GameEvent> Events);

public sealed class TensionChoiceResolver(IGameEffectApplier? effectApplier = null)
{
    private readonly IGameEffectApplier _effectApplier = effectApplier ?? new GameEffectApplier();

    public TensionChoiceResult Choose(
        GameState state,
        ChooseTensionOptionCommand command,
        int startingEventSequence = 1)
    {
        if (state.IsComplete)
        {
            throw new TensionChoiceValidationException("Completed campaigns cannot resolve operational opportunities.");
        }

        var card = state.ActiveTensions.FirstOrDefault(activeCard => activeCard.Id == command.CardId);
        if (card is null)
        {
            throw new TensionChoiceValidationException($"Operational opportunity '{command.CardId}' is not active.");
        }

        var option = card.Options.FirstOrDefault(candidate => candidate.Id == command.OptionId);
        if (option is null)
        {
            throw new TensionChoiceValidationException($"Operational opportunity '{card.Id}' does not contain option '{command.OptionId}'.");
        }

        var decision = new TensionDecision(
            state.TurnNumber,
            command.Side,
            card.Id,
            card.Title,
            option.Id,
            option.Label,
            option.Effects.Select(effect => effect.Description).ToArray());

        var choiceEvent = new GameEvent(
            startingEventSequence,
            GameEventType.Tension,
            GameEventScope.Campaign,
            command.Side,
            null,
            null,
            $"Operational opportunity resolved: {card.Title} - {option.Label}.",
            new Dictionary<string, object?>
            {
                ["cardId"] = card.Id,
                ["optionId"] = option.Id,
                ["appliedEffects"] = decision.AppliedEffects
            });

        var applied = _effectApplier.Apply(state, option.Effects, startingEventSequence + 1);
        var nextState = applied.State with
        {
            ActiveTensions = applied.State.ActiveTensions
                .Where(activeCard => activeCard.Id != card.Id)
                .OrderBy(activeCard => activeCard.Id, StringComparer.Ordinal)
                .ToArray(),
            TensionHistory = applied.State.TensionHistory
                .Append(decision)
                .OrderBy(history => history.TurnNumber)
                .ThenBy(history => history.CardId, StringComparer.Ordinal)
                .ToArray()
        };

        return new TensionChoiceResult(
            nextState,
            decision,
            new[] { choiceEvent }.Concat(applied.Events).ToArray());
    }
}
