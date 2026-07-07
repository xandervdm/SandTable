namespace SandTable.Engine;

public sealed class TurnResolver(ITensionGenerator? tensionGenerator = null)
{
    private const int MaximumActiveTensionCards = 2;
    private readonly ITensionGenerator _tensionGenerator = tensionGenerator ?? new BasicTensionGenerator();

    public TurnResolution Resolve(
        GameState startingState,
        IReadOnlyCollection<SubmittedCommand> humanCommands,
        IReadOnlyCollection<SubmittedCommand> aiCommands,
        int randomSeed,
        TensionCardCatalog? tensionCards = null)
    {
        if (startingState.IsComplete)
        {
            throw new InvalidOperationException("Completed campaigns cannot resolve another turn.");
        }

        var random = new Random(randomSeed);
        var regions = startingState.Regions.ToDictionary(region => region.Id, StringComparer.Ordinal);
        var units = startingState.Units.ToDictionary(unit => unit.Id, StringComparer.Ordinal);
        var events = new List<GameEvent>();
        var resolvedCommands = new List<ResolvedCommand>();

        var plannedCommands = humanCommands
            .Concat(aiCommands)
            .OrderBy(command => command.Source)
            .ThenBy(command => command.Sequence)
            .ToArray();

        foreach (var command in plannedCommands)
        {
            var resolvedCommand = command.CommandType switch
            {
                OrderType.Move => ResolveMove(command, startingState, regions, units, events),
                OrderType.Attack => ResolveAttack(command, startingState, regions, units, events, random),
                OrderType.Resupply => ResolveResupply(command, units, events),
                OrderType.Recon => AddSimpleEvent(command, GameEventType.Recon, GameEventScope.Region, events, "Recon patrols report no major change."),
                OrderType.Support => AddSimpleEvent(command, GameEventType.System, GameEventScope.Unit, events, "Support order acknowledged."),
                _ => AddSimpleEvent(command, GameEventType.System, GameEventScope.Unit, events, "Unit holds position.")
            };

            resolvedCommands.Add(resolvedCommand);
        }

        CaptureOccupiedRegions(regions, units);

        var result = ResolveCampaignResult(startingState, regions);
        var nextState = startingState with
        {
            TurnNumber = startingState.TurnNumber + 1,
            CampaignDate = startingState.CampaignDate.AddDays(7),
            Regions = regions.Values.OrderBy(region => region.Id, StringComparer.Ordinal).ToArray(),
            Units = units.Values.OrderBy(unit => unit.Id, StringComparer.Ordinal).ToArray(),
            IsComplete = result is not null,
            Result = result,
            CampaignModifiers = AgeCampaignModifiers(startingState.CampaignModifiers)
        };

        if (result is not null)
        {
            events.Add(new GameEvent(
                events.Count + 1,
                GameEventType.Victory,
                GameEventScope.Campaign,
                startingState.PlayerSide,
                startingState.VictoryRegionId,
                null,
                $"Campaign ended with {result}.",
                new Dictionary<string, object?> { ["result"] = result }));
        }
        else
        {
            var generatedTensions = _tensionGenerator.Generate(
                nextState,
                tensionCards ?? new TensionCardCatalog(Array.Empty<TensionCardDefinition>()),
                randomSeed,
                MaximumActiveTensionCards);
            if (generatedTensions.Count > 0 || nextState.ActiveTensions.Count > MaximumActiveTensionCards)
            {
                nextState = nextState with
                {
                    ActiveTensions = nextState.ActiveTensions
                        .Concat(generatedTensions)
                        .GroupBy(card => card.Id, StringComparer.Ordinal)
                        .Select(group => group.First())
                        .Take(MaximumActiveTensionCards)
                        .OrderBy(card => card.Id, StringComparer.Ordinal)
                        .ToArray()
                };

                foreach (var tension in generatedTensions)
                {
                    events.Add(new GameEvent(
                        events.Count + 1,
                        GameEventType.System,
                        GameEventScope.Campaign,
                        startingState.PlayerSide,
                        null,
                        null,
                        $"Operational opportunity emerged: {tension.Title}.",
                        new Dictionary<string, object?>
                        {
                            ["cardId"] = tension.Id,
                            ["category"] = tension.Category.ToString()
                        }));
                }
            }
        }

        return new TurnResolution(
            startingState,
            nextState,
            resolvedCommands,
            events,
            BuildSummary(startingState, nextState, events));
    }

    private static ResolvedCommand ResolveMove(
        SubmittedCommand command,
        GameState startingState,
        Dictionary<string, RegionState> regions,
        Dictionary<string, UnitState> units,
        List<GameEvent> events)
    {
        if (!TryGetUnitCommandContext(command, regions, units, out var unit, out var currentRegion, out var targetRegion, out var rejection))
        {
            return Reject(command, rejection);
        }

        if (!startingState.Regions.First(region => region.Id == currentRegion.Id).AdjacentRegionIds.Contains(targetRegion.Id, StringComparer.Ordinal))
        {
            return Reject(command, $"Region '{targetRegion.Id}' is not adjacent to '{currentRegion.Id}'.");
        }

        var occupiedByEnemy = startingState.Units.Any(other =>
            other.Side != unit.Side
            && other.Side != Side.Neutral
            && other.Status != UnitStatus.Destroyed
            && other.RegionId == targetRegion.Id);

        if (occupiedByEnemy)
        {
            return Reject(command, $"Region '{targetRegion.Id}' is occupied by enemy forces. Use Attack instead.");
        }

        units[unit.Id] = unit with
        {
            RegionId = targetRegion.Id,
            Supply = Math.Max(0, unit.Supply - 1)
        };

        regions[targetRegion.Id] = targetRegion with { Owner = unit.Side };
        events.Add(new GameEvent(
            events.Count + 1,
            GameEventType.Movement,
            GameEventScope.Unit,
            unit.Side,
            targetRegion.Id,
            unit.Id,
            $"{unit.Name} moved to {targetRegion.Name}.",
            new Dictionary<string, object?>
            {
                ["fromRegionId"] = currentRegion.Id,
                ["toRegionId"] = targetRegion.Id
            }));

        return new ResolvedCommand(command, Accepted: true, RejectionReason: null);
    }

    private static ResolvedCommand ResolveAttack(
        SubmittedCommand command,
        GameState startingState,
        Dictionary<string, RegionState> regions,
        Dictionary<string, UnitState> units,
        List<GameEvent> events,
        Random random)
    {
        if (!TryGetUnitCommandContext(command, regions, units, out var attacker, out var currentRegion, out var targetRegion, out var rejection))
        {
            return Reject(command, rejection);
        }

        if (!startingState.Regions.First(region => region.Id == currentRegion.Id).AdjacentRegionIds.Contains(targetRegion.Id, StringComparer.Ordinal))
        {
            return Reject(command, $"Region '{targetRegion.Id}' is not adjacent to '{currentRegion.Id}'.");
        }

        var defenders = units.Values
            .Where(unit => unit.Side != attacker.Side
                && unit.Side != Side.Neutral
                && unit.Status != UnitStatus.Destroyed
                && unit.RegionId == targetRegion.Id)
            .OrderByDescending(unit => unit.Defence + unit.Strength)
            .ToArray();

        if (defenders.Length == 0)
        {
            units[attacker.Id] = attacker with
            {
                RegionId = targetRegion.Id,
                Supply = Math.Max(0, attacker.Supply - 1)
            };
            regions[targetRegion.Id] = targetRegion with { Owner = attacker.Side };
            events.Add(new GameEvent(
                events.Count + 1,
                GameEventType.Movement,
                GameEventScope.Region,
                attacker.Side,
                targetRegion.Id,
                attacker.Id,
                $"{attacker.Name} occupied {targetRegion.Name}.",
                new Dictionary<string, object?> { ["toRegionId"] = targetRegion.Id }));
            return new ResolvedCommand(command, Accepted: true, RejectionReason: null);
        }

        var defender = defenders[0];
        var fortifiedBonus = targetRegion.Features.Contains("Fortified", StringComparer.Ordinal) ? 1 : 0;
        var attackScore = attacker.Attack + Math.Max(0, attacker.Strength / 2) + random.Next(0, 3);
        var defenceScore = defender.Defence + Math.Max(0, defender.Strength / 2) + fortifiedBonus + random.Next(0, 3);
        var attackerDamage = attackScore >= defenceScore ? 1 : 2;
        var defenderDamage = attackScore >= defenceScore ? 3 : 1;

        var nextAttackerStrength = Math.Max(0, attacker.Strength - attackerDamage);
        var nextDefenderStrength = Math.Max(0, defender.Strength - defenderDamage);
        units[attacker.Id] = attacker with
        {
            Strength = nextAttackerStrength,
            Supply = Math.Max(0, attacker.Supply - 2),
            Status = nextAttackerStrength == 0 ? UnitStatus.Destroyed : UnitStatus.Ready
        };
        units[defender.Id] = defender with
        {
            Strength = nextDefenderStrength,
            Status = nextDefenderStrength == 0 ? UnitStatus.Destroyed : UnitStatus.Disrupted
        };

        if (nextDefenderStrength == 0 && nextAttackerStrength > 0)
        {
            units[attacker.Id] = units[attacker.Id] with { RegionId = targetRegion.Id };
            regions[targetRegion.Id] = targetRegion with { Owner = attacker.Side };
        }

        var outcome = attackScore >= defenceScore ? "won" : "stalled";
        events.Add(new GameEvent(
            events.Count + 1,
            GameEventType.Battle,
            GameEventScope.Region,
            attacker.Side,
            targetRegion.Id,
            attacker.Id,
            $"{attacker.Name} {outcome} an attack at {targetRegion.Name}.",
            new Dictionary<string, object?>
            {
                ["attackerUnitId"] = attacker.Id,
                ["defenderUnitId"] = defender.Id,
                ["attackScore"] = attackScore,
                ["defenceScore"] = defenceScore
            }));

        return new ResolvedCommand(command, Accepted: true, RejectionReason: null);
    }

    private static ResolvedCommand ResolveResupply(
        SubmittedCommand command,
        Dictionary<string, UnitState> units,
        List<GameEvent> events)
    {
        if (command.UnitId is null || !units.TryGetValue(command.UnitId, out var unit))
        {
            return Reject(command, "Command must reference a known unit.");
        }

        units[unit.Id] = unit with
        {
            Supply = Math.Min(10, unit.Supply + 2),
            Morale = Math.Min(10, unit.Morale + 1)
        };
        events.Add(new GameEvent(
            events.Count + 1,
            GameEventType.Supply,
            GameEventScope.Unit,
            unit.Side,
            unit.RegionId,
            unit.Id,
            $"{unit.Name} replenished supplies.",
            new Dictionary<string, object?> { ["unitId"] = unit.Id }));

        return new ResolvedCommand(command, Accepted: true, RejectionReason: null);
    }

    private static ResolvedCommand AddSimpleEvent(
        SubmittedCommand command,
        GameEventType eventType,
        GameEventScope eventScope,
        List<GameEvent> events,
        string summary)
    {
        events.Add(new GameEvent(
            events.Count + 1,
            eventType,
            eventScope,
            command.Side,
            command.TargetRegionId ?? command.RegionId,
            command.UnitId,
            summary,
            new Dictionary<string, object?>()));
        return new ResolvedCommand(command, Accepted: true, RejectionReason: null);
    }

    private static bool TryGetUnitCommandContext(
        SubmittedCommand command,
        Dictionary<string, RegionState> regions,
        Dictionary<string, UnitState> units,
        out UnitState unit,
        out RegionState currentRegion,
        out RegionState targetRegion,
        out string rejection)
    {
        unit = default!;
        currentRegion = default!;
        targetRegion = default!;
        rejection = string.Empty;

        if (command.UnitId is null || !units.TryGetValue(command.UnitId, out var resolvedUnit))
        {
            rejection = "Command must reference a known unit.";
            return false;
        }

        unit = resolvedUnit;
        if (unit.Side != command.Side)
        {
            rejection = $"Unit '{unit.Id}' does not belong to side '{command.Side}'.";
            return false;
        }

        if (unit.Status == UnitStatus.Destroyed || unit.Strength <= 0)
        {
            rejection = $"Unit '{unit.Id}' has been destroyed.";
            return false;
        }

        if (!regions.TryGetValue(unit.RegionId, out var resolvedCurrentRegion))
        {
            rejection = $"Unit '{unit.Id}' is in unknown region '{unit.RegionId}'.";
            return false;
        }

        currentRegion = resolvedCurrentRegion;
        if (command.TargetRegionId is null || !regions.TryGetValue(command.TargetRegionId, out var resolvedTargetRegion))
        {
            rejection = "Command must reference a known target region.";
            return false;
        }

        targetRegion = resolvedTargetRegion;
        return true;
    }

    private static ResolvedCommand Reject(SubmittedCommand command, string reason)
    {
        return new ResolvedCommand(command, Accepted: false, RejectionReason: reason);
    }

    private static void CaptureOccupiedRegions(Dictionary<string, RegionState> regions, Dictionary<string, UnitState> units)
    {
        foreach (var region in regions.Values.ToArray())
        {
            var activeUnits = units.Values
                .Where(unit => unit.Status != UnitStatus.Destroyed && unit.RegionId == region.Id)
                .ToArray();
            var occupyingSides = activeUnits.Select(unit => unit.Side).Distinct().ToArray();

            if (occupyingSides.Length == 1 && occupyingSides[0] is Side.Axis or Side.Allies)
            {
                regions[region.Id] = region with { Owner = occupyingSides[0] };
            }
        }
    }

    private static string? ResolveCampaignResult(GameState startingState, Dictionary<string, RegionState> regions)
    {
        if (startingState.VictoryRegionId is not null
            && regions.TryGetValue(startingState.VictoryRegionId, out var victoryRegion)
            && victoryRegion.Owner == startingState.PlayerSide)
        {
            return "Victory";
        }

        return startingState.TurnNumber >= startingState.MaxTurns ? "Defeat" : null;
    }

    private static IReadOnlyList<CampaignModifier> AgeCampaignModifiers(IReadOnlyList<CampaignModifier> modifiers)
    {
        return modifiers
            .Select(modifier => modifier with { RemainingTurns = modifier.RemainingTurns - 1 })
            .Where(modifier => modifier.RemainingTurns > 0)
            .OrderBy(modifier => modifier.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static string BuildSummary(GameState startingState, GameState nextState, IReadOnlyList<GameEvent> events)
    {
        var battles = events.Count(gameEvent => gameEvent.EventType == GameEventType.Battle);
        var movements = events.Count(gameEvent => gameEvent.EventType == GameEventType.Movement);
        var tensions = nextState.ActiveTensions.Count;
        var ending = nextState.IsComplete ? $" Campaign result: {nextState.Result}." : string.Empty;
        var opportunities = tensions > 0 ? $" {tensions} operational opportunities active." : string.Empty;
        return $"Turn {startingState.TurnNumber} resolved: {battles} battles, {movements} movements.{opportunities}{ending}";
    }
}
