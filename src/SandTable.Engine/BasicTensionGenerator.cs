namespace SandTable.Engine;

public interface ITensionGenerator
{
    IReadOnlyList<StrategicTensionCard> Generate(
        GameState state,
        TensionCardCatalog catalog,
        int randomSeed,
        int maxCards);
}

public sealed class BasicTensionGenerator : ITensionGenerator
{
    public IReadOnlyList<StrategicTensionCard> Generate(
        GameState state,
        TensionCardCatalog catalog,
        int randomSeed,
        int maxCards)
    {
        var remainingSlots = Math.Max(0, maxCards - state.ActiveTensions.Count);
        if (remainingSlots == 0)
        {
            return Array.Empty<StrategicTensionCard>();
        }

        var existingCardIds = state.ActiveTensions
            .Select(card => card.Id)
            .Concat(state.TensionHistory.Select(decision => decision.CardId))
            .ToHashSet(StringComparer.Ordinal);

        var candidates = catalog.Cards
            .Select(definition => TryCreateCard(state, definition))
            .Where(card => card is not null)
            .Select(card => card!)
            .Where(card => !existingCardIds.Contains(card.Id))
            .GroupBy(card => card.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(card => StableScore(randomSeed, state.TurnNumber, card.Id))
            .ThenBy(card => card.Id, StringComparer.Ordinal)
            .ToArray();

        if (candidates.Length == 0)
        {
            return Array.Empty<StrategicTensionCard>();
        }

        var desiredCards = 1 + StableScore(randomSeed, state.TurnNumber, "card-count") % 2;
        return candidates
            .Take(Math.Min(remainingSlots, desiredCards))
            .ToArray();
    }

    private static StrategicTensionCard? TryCreateCard(GameState state, TensionCardDefinition definition)
    {
        var context = new TensionTemplateContext(
            ResolveUnit(state, definition.Targeting.UnitSelector),
            ResolveUnit(state, definition.Targeting.EnemyUnitSelector),
            ResolveRegion(state, definition.Targeting.RegionSelector));

        if (RequiresTarget(definition.Targeting.UnitSelector, context.Unit)
            || RequiresTarget(definition.Targeting.EnemyUnitSelector, context.EnemyUnit)
            || RequiresTarget(definition.Targeting.RegionSelector, context.Region))
        {
            return null;
        }

        return new StrategicTensionCard(
            definition.Id,
            ApplyTemplate(definition.Title, context),
            ApplyTemplate(definition.Description, context),
            definition.Category,
            definition.Trigger,
            definition.Options
                .Select(option => new TensionOption(
                    option.Id,
                    ApplyTemplate(option.Label, context),
                    ApplyTemplate(option.Description, context),
                    option.Effects.Select(effect => CreateEffect(effect, context)).ToArray()))
                .ToArray());
    }

    private static bool RequiresTarget(string? selector, object? target)
    {
        return !string.IsNullOrWhiteSpace(selector) && target is null;
    }

    private static GameEffect CreateEffect(GameEffectDefinition definition, TensionTemplateContext context)
    {
        var description = ApplyTemplate(definition.Description, context);
        return definition.EffectType switch
        {
            "addResource" => new AddResourceEffect(
                RequiredValue(definition.Resource, definition.EffectType, nameof(definition.Resource)),
                RequiredValue(definition.Amount, definition.EffectType, nameof(definition.Amount)),
                description),

            "addCampaignModifier" => new AddCampaignModifierEffect(
                Required(definition.ModifierId, definition.EffectType, nameof(definition.ModifierId)),
                ApplyTemplate(Required(definition.Name, definition.EffectType, nameof(definition.Name)), context),
                RequiredValue(definition.DurationTurns, definition.EffectType, nameof(definition.DurationTurns)),
                definition.Values ?? new Dictionary<string, int>(),
                description),

            "modifyUnitStat" => new ModifyUnitStatEffect(
                Required(ResolveEffectUnit(context, definition.UnitSelector)?.Id, definition.EffectType, nameof(definition.UnitSelector)),
                RequiredValue(definition.Stat, definition.EffectType, nameof(definition.Stat)),
                RequiredValue(definition.Amount, definition.EffectType, nameof(definition.Amount)),
                description),

            "modifyRegion" => new ModifyRegionEffect(
                Required(ResolveEffectRegion(context, definition.RegionSelector)?.Id, definition.EffectType, nameof(definition.RegionSelector)),
                definition.Owner,
                definition.SupplyValueDelta ?? 0,
                definition.AddFeature,
                description),

            "addGameEvent" => new AddGameEventEffect(
                ApplyTemplate(Required(definition.Summary, definition.EffectType, nameof(definition.Summary)), context),
                RequiredValue(definition.EventType, definition.EffectType, nameof(definition.EventType)),
                RequiredValue(definition.EventScope, definition.EffectType, nameof(definition.EventScope)),
                ResolveSide(context, definition.SideSelector),
                ResolveEffectRegion(context, definition.RegionSelector)?.Id,
                ResolveEffectUnit(context, definition.UnitSelector)?.Id,
                ApplyPayloadTemplate(definition.Payload ?? new Dictionary<string, object?>(), context),
                description),

            _ => throw new InvalidOperationException($"Unsupported tension effect type '{definition.EffectType}'.")
        };
    }

    private static UnitState? ResolveUnit(GameState state, string? selector)
    {
        return selector switch
        {
            null or "" => null,
            "playerArmourLowestSupply" => state.Units
                .Where(unit => unit.Side == state.PlayerSide && unit.Type == UnitType.Armour && unit.Status != UnitStatus.Destroyed)
                .OrderBy(unit => unit.Supply)
                .ThenBy(unit => unit.Id, StringComparer.Ordinal)
                .FirstOrDefault(),
            "playerLineLowestDefence" => state.Units
                .Where(unit => unit.Side == state.PlayerSide && unit.Type is UnitType.Infantry or UnitType.Armour && unit.Status != UnitStatus.Destroyed)
                .OrderBy(unit => unit.Defence)
                .ThenBy(unit => unit.Id, StringComparer.Ordinal)
                .FirstOrDefault(),
            "playerAirOrArmourLowestMorale" => state.Units
                .Where(unit => unit.Side == state.PlayerSide && unit.Type is UnitType.AirWing or UnitType.Armour && unit.Status != UnitStatus.Destroyed)
                .OrderBy(unit => unit.Morale)
                .ThenBy(unit => unit.Id, StringComparer.Ordinal)
                .FirstOrDefault(),
            "enemyHighestSupply" => state.Units
                .Where(unit => unit.Side == state.EnemySide && unit.Status != UnitStatus.Destroyed)
                .OrderByDescending(unit => unit.Supply)
                .ThenBy(unit => unit.Id, StringComparer.Ordinal)
                .FirstOrDefault(),
            _ => throw new InvalidOperationException($"Unsupported unit selector '{selector}'.")
        };
    }

    private static RegionState? ResolveRegion(GameState state, string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return null;
        }

        var regions = state.Regions.ToDictionary(region => region.Id, StringComparer.Ordinal);
        return selector switch
        {
            "playerFrontlineHighestVictory" => state.Regions
                .Where(region => region.Owner == state.PlayerSide)
                .Where(region => region.AdjacentRegionIds
                    .Select(adjacentId => regions.TryGetValue(adjacentId, out var adjacent) ? adjacent : null)
                    .Any(adjacent => adjacent is not null && adjacent.Owner == state.EnemySide))
                .OrderByDescending(region => region.VictoryPoints)
                .ThenBy(region => region.Id, StringComparer.Ordinal)
                .FirstOrDefault(),
            _ => throw new InvalidOperationException($"Unsupported region selector '{selector}'.")
        };
    }

    private static UnitState? ResolveEffectUnit(TensionTemplateContext context, string? selector)
    {
        return selector switch
        {
            null or "" => null,
            "unit" => context.Unit,
            "enemyUnit" => context.EnemyUnit,
            _ => throw new InvalidOperationException($"Unsupported effect unit selector '{selector}'.")
        };
    }

    private static RegionState? ResolveEffectRegion(TensionTemplateContext context, string? selector)
    {
        return selector switch
        {
            null or "" => null,
            "region" => context.Region,
            _ => throw new InvalidOperationException($"Unsupported effect region selector '{selector}'.")
        };
    }

    private static Side? ResolveSide(TensionTemplateContext context, string? selector)
    {
        return selector switch
        {
            null or "" => null,
            "unit" => context.Unit?.Side,
            "enemyUnit" => context.EnemyUnit?.Side,
            _ => throw new InvalidOperationException($"Unsupported side selector '{selector}'.")
        };
    }

    private static IReadOnlyDictionary<string, object?> ApplyPayloadTemplate(
        IReadOnlyDictionary<string, object?> payload,
        TensionTemplateContext context)
    {
        return payload.ToDictionary(
            pair => pair.Key,
            pair => pair.Value is string text ? ApplyTemplate(text, context) : pair.Value,
            StringComparer.Ordinal);
    }

    private static string ApplyTemplate(string value, TensionTemplateContext context)
    {
        return value
            .Replace("{unit.id}", context.Unit?.Id ?? string.Empty, StringComparison.Ordinal)
            .Replace("{unit.name}", context.Unit?.Name ?? string.Empty, StringComparison.Ordinal)
            .Replace("{unit.regionId}", context.Unit?.RegionId ?? string.Empty, StringComparison.Ordinal)
            .Replace("{enemyUnit.id}", context.EnemyUnit?.Id ?? string.Empty, StringComparison.Ordinal)
            .Replace("{enemyUnit.name}", context.EnemyUnit?.Name ?? string.Empty, StringComparison.Ordinal)
            .Replace("{enemyUnit.regionId}", context.EnemyUnit?.RegionId ?? string.Empty, StringComparison.Ordinal)
            .Replace("{region.id}", context.Region?.Id ?? string.Empty, StringComparison.Ordinal)
            .Replace("{region.name}", context.Region?.Name ?? string.Empty, StringComparison.Ordinal);
    }

    private static T Required<T>(T? value, string effectType, string propertyName)
    {
        return value ?? throw new InvalidOperationException($"Effect '{effectType}' requires '{propertyName}'.");
    }

    private static T RequiredValue<T>(T? value, string effectType, string propertyName)
        where T : struct
    {
        return value ?? throw new InvalidOperationException($"Effect '{effectType}' requires '{propertyName}'.");
    }

    private sealed record TensionTemplateContext(
        UnitState? Unit,
        UnitState? EnemyUnit,
        RegionState? Region);

    private static int StableScore(int randomSeed, int turnNumber, string value)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var character in $"{randomSeed}:{turnNumber}:{value}")
            {
                hash ^= character;
                hash *= 16777619u;
            }

            return (int)(hash & 0x7fffffffu);
        }
    }
}
