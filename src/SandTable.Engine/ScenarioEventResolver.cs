namespace SandTable.Engine;

public static class ScenarioEventResolver
{
    public static GameState Apply(
        GameState state,
        ScenarioEventCatalog? catalog,
        ScenarioEventPhase phase,
        List<GameEvent> events)
    {
        if (catalog is null)
        {
            return state;
        }

        foreach (var definition in catalog.Events
            .Where(candidate => candidate.Trigger.Turn == state.TurnNumber
                && candidate.Trigger.Phase == phase
                && !state.ScenarioEventHistory.Contains(candidate.Id, StringComparer.Ordinal)))
        {
            if (!definition.Conditions.All(condition => IsMet(state, condition)))
            {
                continue;
            }

            var reserves = state.Reserves.ToDictionary(reserve => reserve.ReserveId, StringComparer.Ordinal);
            foreach (var effect in definition.Effects)
            {
                if (effect.EffectType == "setReserveStatus"
                    && effect.ReserveId is not null
                    && effect.ReserveStatus.HasValue
                    && reserves.TryGetValue(effect.ReserveId, out var reserve))
                {
                    reserves[effect.ReserveId] = reserve with { Status = effect.ReserveStatus.Value };
                }
            }

            state = state with
            {
                Reserves = reserves.Values.OrderBy(reserve => reserve.ReserveId, StringComparer.Ordinal).ToArray(),
                ScenarioEventHistory = state.ScenarioEventHistory.Append(definition.Id).ToArray()
            };
            events.Add(new GameEvent(
                events.Count + 1,
                GameEventType.Scenario,
                GameEventScope.Campaign,
                null,
                null,
                null,
                definition.Message,
                new Dictionary<string, object?>
                {
                    ["scenarioEventId"] = definition.Id,
                    ["phase"] = phase.ToString(),
                    ["effects"] = definition.Effects.Select(effect => effect.Description).ToArray()
                }));
        }

        return state;
    }

    private static bool IsMet(GameState state, ScenarioEventCondition condition) => condition.Type switch
    {
        "regionControlled" when condition.RegionId is not null && condition.Side.HasValue =>
            state.Regions.Any(region => region.Id == condition.RegionId && region.Owner == ResolveSide(state, condition.Side.Value)),
        "reserveAvailable" when condition.ReserveId is not null =>
            state.Reserves.Any(reserve => reserve.ReserveId == condition.ReserveId && reserve.Status == ReserveStatus.Available),
        _ => false
    };

    private static Side ResolveSide(GameState state, VictorySideSelector selector) => selector switch
    {
        VictorySideSelector.Player => state.PlayerSide,
        VictorySideSelector.Enemy => state.EnemySide,
        VictorySideSelector.Axis => Side.Axis,
        VictorySideSelector.Allies => Side.Allies,
        _ => Side.Neutral
    };
}
