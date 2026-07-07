namespace SandTable.Engine;

public interface IGameEffectApplier
{
    EffectApplicationResult Apply(
        GameState state,
        IReadOnlyCollection<GameEffect> effects,
        int startingEventSequence);
}

public sealed record EffectApplicationResult(
    GameState State,
    IReadOnlyList<GameEvent> Events);

public sealed class GameEffectApplier : IGameEffectApplier
{
    public EffectApplicationResult Apply(
        GameState state,
        IReadOnlyCollection<GameEffect> effects,
        int startingEventSequence)
    {
        var resources = state.Resources;
        var units = state.Units.ToDictionary(unit => unit.Id, StringComparer.Ordinal);
        var regions = state.Regions.ToDictionary(region => region.Id, StringComparer.Ordinal);
        var modifiers = state.CampaignModifiers.ToList();
        var events = new List<GameEvent>();

        foreach (var effect in effects)
        {
            switch (effect)
            {
                case AddResourceEffect addResource:
                    resources = ApplyResourceEffect(resources, addResource);
                    break;

                case ModifyUnitStatEffect modifyUnit:
                    ApplyUnitEffect(units, modifyUnit);
                    break;

                case ModifyRegionEffect modifyRegion:
                    ApplyRegionEffect(regions, modifyRegion);
                    break;

                case AddCampaignModifierEffect addModifier:
                    modifiers.RemoveAll(modifier => modifier.Id == addModifier.ModifierId);
                    modifiers.Add(new CampaignModifier(
                        addModifier.ModifierId,
                        addModifier.Name,
                        Math.Max(1, addModifier.DurationTurns),
                        addModifier.Values));
                    break;

                case AddGameEventEffect addEvent:
                    events.Add(new GameEvent(
                        startingEventSequence + events.Count,
                        addEvent.EventType,
                        addEvent.EventScope,
                        addEvent.Side,
                        addEvent.RegionId,
                        addEvent.UnitId,
                        addEvent.Summary,
                        addEvent.Payload));
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported game effect '{effect.GetType().Name}'.");
            }
        }

        return new EffectApplicationResult(
            state with
            {
                Resources = resources,
                Regions = regions.Values.OrderBy(region => region.Id, StringComparer.Ordinal).ToArray(),
                Units = units.Values.OrderBy(unit => unit.Id, StringComparer.Ordinal).ToArray(),
                CampaignModifiers = modifiers
                    .OrderBy(modifier => modifier.Id, StringComparer.Ordinal)
                    .ToArray()
            },
            events);
    }

    private static Resources ApplyResourceEffect(Resources resources, AddResourceEffect effect)
    {
        return effect.Resource switch
        {
            ResourceType.Supplies => resources with { Supplies = ClampNonNegative(resources.Supplies + effect.Amount) },
            ResourceType.Manpower => resources with { Manpower = ClampNonNegative(resources.Manpower + effect.Amount) },
            ResourceType.Fuel => resources with { Fuel = ClampNonNegative(resources.Fuel + effect.Amount) },
            ResourceType.Industry => resources with { Industry = ClampNonNegative(resources.Industry + effect.Amount) },
            ResourceType.CommandPoints => resources with { CommandPoints = ClampNonNegative(resources.CommandPoints + effect.Amount) },
            _ => resources
        };
    }

    private static void ApplyUnitEffect(Dictionary<string, UnitState> units, ModifyUnitStatEffect effect)
    {
        if (!units.TryGetValue(effect.UnitId, out var unit))
        {
            throw new InvalidOperationException($"Tension effect references unknown unit '{effect.UnitId}'.");
        }

        units[unit.Id] = effect.Stat switch
        {
            UnitStat.Strength => unit with { Strength = Clamp(unit.Strength + effect.Amount, 0, unit.MaxStrength) },
            UnitStat.Movement => unit with { Movement = ClampNonNegative(unit.Movement + effect.Amount) },
            UnitStat.Attack => unit with { Attack = ClampNonNegative(unit.Attack + effect.Amount) },
            UnitStat.Defence => unit with { Defence = ClampNonNegative(unit.Defence + effect.Amount) },
            UnitStat.Supply => unit with { Supply = Clamp(unit.Supply + effect.Amount, 0, 10) },
            UnitStat.Morale => unit with { Morale = Clamp(unit.Morale + effect.Amount, 0, 10) },
            UnitStat.Experience => unit with { Experience = ClampNonNegative(unit.Experience + effect.Amount) },
            _ => unit
        };
    }

    private static void ApplyRegionEffect(Dictionary<string, RegionState> regions, ModifyRegionEffect effect)
    {
        if (!regions.TryGetValue(effect.RegionId, out var region))
        {
            throw new InvalidOperationException($"Tension effect references unknown region '{effect.RegionId}'.");
        }

        var features = region.Features.ToList();
        if (!string.IsNullOrWhiteSpace(effect.AddFeature)
            && !features.Contains(effect.AddFeature, StringComparer.Ordinal))
        {
            features.Add(effect.AddFeature);
        }

        regions[region.Id] = region with
        {
            Owner = effect.Owner ?? region.Owner,
            SupplyValue = ClampNonNegative(region.SupplyValue + effect.SupplyValueDelta),
            Features = features.OrderBy(feature => feature, StringComparer.Ordinal).ToArray()
        };
    }

    private static int ClampNonNegative(int value)
    {
        return Math.Max(0, value);
    }

    private static int Clamp(int value, int minimum, int maximum)
    {
        return Math.Min(maximum, Math.Max(minimum, value));
    }
}
