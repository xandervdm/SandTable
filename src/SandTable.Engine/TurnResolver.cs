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
        var supportByRegion = new Dictionary<(Side Side, string RegionId), int>();
        var reconByRegion = new HashSet<(Side Side, string RegionId)>();
        var availableResources = startingState.Resources.ToDictionary(
            pair => pair.Key,
            pair => CommandEconomy.CreateTurnBudget(startingState, pair.Key));

        var plannedCommands = humanCommands
            .Concat(aiCommands)
            .OrderBy(command => command.Source)
            .ThenBy(command => command.Sequence)
            .ToArray();

        foreach (var command in plannedCommands)
        {
            var cost = CommandEconomy.CalculateCost(startingState, command);
            if (!availableResources.TryGetValue(command.Side, out var available)
                || !CommandEconomy.CanAfford(available, cost))
            {
                var reason = available is null
                    ? $"Side '{command.Side}' has no resource budget."
                    : $"Command is unaffordable: insufficient {CommandEconomy.DescribeShortfall(available, cost)}.";
                resolvedCommands.Add(new ResolvedCommand(command, Accepted: false, reason, cost));
                continue;
            }

            var resolvedCommand = command.CommandType switch
            {
                OrderType.Move => ResolveMove(command, startingState, regions, units, events),
                OrderType.Attack => ResolveAttack(command, startingState, regions, units, supportByRegion, reconByRegion, events, random),
                OrderType.Resupply => ResolveResupply(command, startingState, regions, units, events),
                OrderType.Recon => ResolveRecon(command, startingState, regions, units, reconByRegion, events),
                OrderType.Support => ResolveSupport(command, regions, units, supportByRegion, events),
                OrderType.Deploy => Reject(command, "Reserve deployment resolution is introduced in Phase 6."),
                _ => ResolveHold(command, units, events)
            };

            if (resolvedCommand.Accepted)
            {
                availableResources[command.Side] = CommandEconomy.Spend(available, cost);
                resolvedCommand = resolvedCommand with { Cost = cost };
            }
            resolvedCommands.Add(resolvedCommand);
        }

        CaptureOccupiedRegions(regions, units);
        ApplySupplyLifecycle(startingState, regions, units, events);

        var nextResources = availableResources.ToDictionary(
            pair => pair.Key,
            pair => pair.Value with { CommandPoints = startingState.Resources[pair.Key].CommandPoints });
        ApplyModifierResourceEffects(startingState, nextResources, events);

        var evaluationState = startingState with
        {
            Resources = nextResources,
            Regions = regions.Values.OrderBy(region => region.Id, StringComparer.Ordinal).ToArray(),
            Units = units.Values.OrderBy(unit => unit.Id, StringComparer.Ordinal).ToArray()
        };
        var (result, victoryProgress) = ResolveCampaignResult(evaluationState);
        var nextTurnNumber = result.HasValue ? startingState.TurnNumber : startingState.TurnNumber + 1;
        var nextState = evaluationState with
        {
            TurnNumber = nextTurnNumber,
            CampaignDate = startingState.CampaignDate.AddDays(7),
            Reserves = startingState.Reserves
                .Select(reserve => reserve.Status == ReserveStatus.Unavailable && reserve.AvailableTurn <= nextTurnNumber
                    ? reserve with { Status = ReserveStatus.Available }
                    : reserve)
                .OrderBy(reserve => reserve.ReserveId, StringComparer.Ordinal)
                .ToArray(),
            IsComplete = result is not null,
            Result = result,
            VictoryProgress = victoryProgress,
            CampaignModifiers = AgeCampaignModifiers(startingState.CampaignModifiers)
        };

        if (result is not null)
        {
            events.Add(new GameEvent(
                events.Count + 1,
                GameEventType.Victory,
                GameEventScope.Campaign,
                null,
                null,
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
                        GameEventType.Tension,
                        GameEventScope.Campaign,
                        null,
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

        var movementCost = command.Payload is MoveCommandPayload move
            ? CommandEconomy.CalculatePathMovementCost(startingState.Routes, move.FromRegionId, move.PathRegionIds)
            : 0;
        var movementAllowance = Math.Max(0,
            unit.Movement
            - CampaignModifierRules.Value(startingState, unit.Side, "tempoCost")
            - (unit.SupplyStatus == UnitSupplyStatus.OutOfSupply ? 1 : 0));
        if (movementCost > movementAllowance)
        {
            return Reject(command, $"Movement cost {movementCost} exceeds effective allowance {movementAllowance}.");
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
            Supply = Math.Max(0, unit.Supply - 1),
            IsEntrenched = false
        };

        var objectiveCaptured = targetRegion.Owner != unit.Side && targetRegion.VictoryPoints > 0;
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
                ["toRegionId"] = targetRegion.Id,
                ["previousOwner"] = targetRegion.Owner.ToString(),
                ["objectiveCaptured"] = objectiveCaptured
            }));

        return new ResolvedCommand(command, Accepted: true, RejectionReason: null, CommandEconomy.Zero);
    }

    private static ResolvedCommand ResolveAttack(
        SubmittedCommand command,
        GameState startingState,
        Dictionary<string, RegionState> regions,
        Dictionary<string, UnitState> units,
        IReadOnlyDictionary<(Side Side, string RegionId), int> supportByRegion,
        IReadOnlySet<(Side Side, string RegionId)> reconByRegion,
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

        var movementCost = command.Payload is AttackCommandPayload attack
            ? CommandEconomy.CalculatePathMovementCost(startingState.Routes, attack.FromRegionId, attack.PathRegionIds)
            : 0;
        var movementAllowance = Math.Max(0,
            attacker.Movement
            - CampaignModifierRules.Value(startingState, attacker.Side, "tempoCost")
            - (attacker.SupplyStatus == UnitSupplyStatus.OutOfSupply ? 1 : 0));
        if (movementCost > movementAllowance)
        {
            return Reject(command, $"Movement cost {movementCost} exceeds effective allowance {movementAllowance}.");
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
            var objectiveCaptured = targetRegion.Owner != attacker.Side && targetRegion.VictoryPoints > 0;
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
                new Dictionary<string, object?>
                {
                    ["fromRegionId"] = currentRegion.Id,
                    ["toRegionId"] = targetRegion.Id,
                    ["previousOwner"] = targetRegion.Owner.ToString(),
                    ["objectiveCaptured"] = objectiveCaptured
                }));
            return new ResolvedCommand(command, Accepted: true, RejectionReason: null, CommandEconomy.Zero);
        }

        var defender = defenders[0];
        var fortifiedBonus = targetRegion.Features.Contains("Fortified", StringComparer.Ordinal) ? 1 : 0;
        var attackerSupport = supportByRegion.GetValueOrDefault((attacker.Side, targetRegion.Id));
        var defenderSupport = supportByRegion.GetValueOrDefault((defender.Side, targetRegion.Id));
        var reconBonus = reconByRegion.Contains((attacker.Side, targetRegion.Id))
            ? 1 + CampaignModifierRules.Value(startingState, attacker.Side, "recon")
            : 0;
        var attackerSupplyPenalty = attacker.SupplyStatus == UnitSupplyStatus.OutOfSupply ? 2 : 0;
        var defenderSupplyPenalty = defender.SupplyStatus == UnitSupplyStatus.OutOfSupply ? 2 : 0;
        var attackScore = attacker.Attack
            + Math.Max(0, attacker.Strength / 2)
            + attackerSupport
            + reconBonus
            + CampaignModifierRules.Value(startingState, attacker.Side, "attack")
            - attackerSupplyPenalty
            + random.Next(0, 3);
        var defenceScore = defender.Defence
            + Math.Max(0, defender.Strength / 2)
            + fortifiedBonus
            + (defender.IsEntrenched ? 2 : 0)
            + defenderSupport
            + CampaignModifierRules.Value(startingState, defender.Side, "defence")
            - defenderSupplyPenalty
            + random.Next(0, 3);
        var attackerDamage = attackScore >= defenceScore ? 1 : 2;
        var defenderDamage = attackScore >= defenceScore ? 3 : 1;

        var nextAttackerStrength = Math.Max(0, attacker.Strength - attackerDamage);
        var nextDefenderStrength = Math.Max(0, defender.Strength - defenderDamage);
        units[attacker.Id] = attacker with
        {
            Strength = nextAttackerStrength,
            Supply = Math.Max(0, attacker.Supply - 2),
            Status = nextAttackerStrength == 0 ? UnitStatus.Destroyed : UnitStatus.Ready,
            IsEntrenched = false
        };
        units[defender.Id] = defender with
        {
            Strength = nextDefenderStrength,
            Status = nextDefenderStrength == 0 ? UnitStatus.Destroyed : UnitStatus.Disrupted
        };

        var objectiveCapturedAfterBattle = nextDefenderStrength == 0
            && nextAttackerStrength > 0
            && targetRegion.Owner != attacker.Side
            && targetRegion.VictoryPoints > 0;
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
                ["fromRegionId"] = currentRegion.Id,
                ["toRegionId"] = targetRegion.Id,
                ["attackScore"] = attackScore,
                ["defenceScore"] = defenceScore,
                ["attackerSupport"] = attackerSupport,
                ["defenderSupport"] = defenderSupport,
                ["reconBonus"] = reconBonus,
                ["attackerSupplyPenalty"] = attackerSupplyPenalty,
                ["defenderSupplyPenalty"] = defenderSupplyPenalty,
                ["attackerDamage"] = attackerDamage,
                ["defenderDamage"] = defenderDamage,
                ["attackerStrength"] = nextAttackerStrength,
                ["defenderStrength"] = nextDefenderStrength,
                ["attackerDestroyed"] = nextAttackerStrength == 0,
                ["defenderDestroyed"] = nextDefenderStrength == 0,
                ["previousOwner"] = targetRegion.Owner.ToString(),
                ["objectiveCaptured"] = objectiveCapturedAfterBattle
            }));

        return new ResolvedCommand(command, Accepted: true, RejectionReason: null, CommandEconomy.Zero);
    }

    private static ResolvedCommand ResolveResupply(
        SubmittedCommand command,
        GameState startingState,
        IReadOnlyDictionary<string, RegionState> regions,
        Dictionary<string, UnitState> units,
        List<GameEvent> events)
    {
        if (command.UnitId is null || !units.TryGetValue(command.UnitId, out var unit))
        {
            return Reject(command, "Command must reference a known unit.");
        }
        if (command.RegionId != unit.RegionId || unit.Side != command.Side || unit.Status == UnitStatus.Destroyed)
        {
            return Reject(command, "Resupply must reference the active unit's current region and side.");
        }

        var trace = SupplyTracer.Trace(regions.Values.ToArray(), startingState.Routes, unit.Side, unit.RegionId);
        if (!trace.IsConnected)
        {
            return Reject(command, $"{unit.Name} has no controlled supply route and cannot resupply.");
        }

        units[unit.Id] = unit with
        {
            Supply = Math.Min(10, unit.Supply + 4),
            Morale = Math.Min(10, unit.Morale + 1),
            SupplyStatus = UnitSupplyStatus.InSupply,
            OutOfSupplyTurns = 0
        };
        events.Add(new GameEvent(
            events.Count + 1,
            GameEventType.Supply,
            GameEventScope.Unit,
            unit.Side,
            unit.RegionId,
            unit.Id,
            $"{unit.Name} replenished supplies.",
            new Dictionary<string, object?>
            {
                ["unitId"] = unit.Id,
                ["sourceRegionId"] = trace.SourceRegionId,
                ["supplyRestored"] = 4
            }));

        return new ResolvedCommand(command, Accepted: true, RejectionReason: null, CommandEconomy.Zero);
    }

    private static ResolvedCommand ResolveSupport(
        SubmittedCommand command,
        IReadOnlyDictionary<string, RegionState> regions,
        Dictionary<string, UnitState> units,
        Dictionary<(Side Side, string RegionId), int> supportByRegion,
        List<GameEvent> events,
        int supportValue = 2)
    {
        if (command.UnitId is null || command.TargetRegionId is null
            || !units.TryGetValue(command.UnitId, out var unit)
            || !regions.TryGetValue(command.TargetRegionId, out var targetRegion))
        {
            return Reject(command, "Support requires a known unit and target region.");
        }
        if (unit.Side != command.Side || unit.Status == UnitStatus.Destroyed
            || !regions[unit.RegionId].AdjacentRegionIds.Append(unit.RegionId).Contains(targetRegion.Id, StringComparer.Ordinal))
        {
            return Reject(command, "Support target must be the active unit's current or adjacent region.");
        }
        supportByRegion[(unit.Side, targetRegion.Id)] = supportByRegion.GetValueOrDefault((unit.Side, targetRegion.Id)) + supportValue;
        units[unit.Id] = unit with { Supply = Math.Max(0, unit.Supply - 1) };
        events.Add(new GameEvent(
            events.Count + 1,
            GameEventType.System,
            GameEventScope.Region,
            unit.Side,
            targetRegion.Id,
            unit.Id,
            $"{unit.Name} provided combat support at {targetRegion.Name}.",
            new Dictionary<string, object?>
            {
                ["effect"] = "CombatSupport",
                ["supportValue"] = supportValue,
                ["targetRegionId"] = targetRegion.Id
            }));
        return new ResolvedCommand(command, Accepted: true, RejectionReason: null, CommandEconomy.Zero);
    }

    private static ResolvedCommand ResolveRecon(
        SubmittedCommand command,
        GameState startingState,
        IReadOnlyDictionary<string, RegionState> regions,
        Dictionary<string, UnitState> units,
        HashSet<(Side Side, string RegionId)> reconByRegion,
        List<GameEvent> events)
    {
        if (command.UnitId is null || command.TargetRegionId is null
            || !units.TryGetValue(command.UnitId, out var unit)
            || !regions.TryGetValue(command.TargetRegionId, out var targetRegion))
        {
            return Reject(command, "Recon requires a known unit and target region.");
        }
        if (unit.Side != command.Side || unit.Status == UnitStatus.Destroyed
            || !regions[unit.RegionId].AdjacentRegionIds.Append(unit.RegionId).Contains(targetRegion.Id, StringComparer.Ordinal))
        {
            return Reject(command, "Recon target must be the active unit's current or adjacent region.");
        }

        reconByRegion.Add((unit.Side, targetRegion.Id));
        var enemyUnits = units.Values
            .Where(candidate => candidate.RegionId == targetRegion.Id
                && candidate.Side != unit.Side
                && candidate.Side != Side.Neutral
                && candidate.Status != UnitStatus.Destroyed)
            .OrderBy(candidate => candidate.Id, StringComparer.Ordinal)
            .ToArray();
        var moralePressure = CampaignModifierRules.Value(startingState, unit.Side, "enemyMoralePressure");
        if (moralePressure > 0)
        {
            foreach (var enemy in enemyUnits)
            {
                units[enemy.Id] = enemy with { Morale = Math.Max(0, enemy.Morale - moralePressure) };
            }
        }
        events.Add(new GameEvent(
            events.Count + 1,
            GameEventType.Recon,
            GameEventScope.Region,
            unit.Side,
            targetRegion.Id,
            unit.Id,
            $"{unit.Name} reconnoitred {targetRegion.Name}: {enemyUnits.Length} enemy units, {enemyUnits.Sum(enemy => enemy.Strength)} strength.",
            new Dictionary<string, object?>
            {
                ["targetRegionId"] = targetRegion.Id,
                ["enemyUnitCount"] = enemyUnits.Length,
                ["enemyStrength"] = enemyUnits.Sum(enemy => enemy.Strength),
                ["attackBonus"] = 1 + CampaignModifierRules.Value(startingState, unit.Side, "recon"),
                ["enemyMoralePressure"] = moralePressure
            }));
        return new ResolvedCommand(command, Accepted: true, RejectionReason: null, CommandEconomy.Zero);
    }

    private static ResolvedCommand ResolveHold(
        SubmittedCommand command,
        Dictionary<string, UnitState> units,
        List<GameEvent> events)
    {
        if (command.UnitId is null || !units.TryGetValue(command.UnitId, out var unit))
        {
            return Reject(command, "Hold requires a known unit.");
        }
        if (command.RegionId != unit.RegionId || unit.Side != command.Side || unit.Status == UnitStatus.Destroyed)
        {
            return Reject(command, "Hold must reference the active unit's current region and side.");
        }
        units[unit.Id] = unit with
        {
            IsEntrenched = true,
            Morale = Math.Min(10, unit.Morale + 1)
        };
        events.Add(new GameEvent(
            events.Count + 1,
            GameEventType.System,
            GameEventScope.Unit,
            unit.Side,
            unit.RegionId,
            unit.Id,
            $"{unit.Name} dug in and prepared its position.",
            new Dictionary<string, object?>
            {
                ["effect"] = "Entrenched",
                ["defenceBonus"] = 2,
                ["moraleRestored"] = 1
            }));
        return new ResolvedCommand(command, Accepted: true, RejectionReason: null, CommandEconomy.Zero);
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
        return new ResolvedCommand(command, Accepted: false, RejectionReason: reason, CommandEconomy.Zero);
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

    private static void ApplySupplyLifecycle(
        GameState startingState,
        IReadOnlyDictionary<string, RegionState> regions,
        Dictionary<string, UnitState> units,
        List<GameEvent> events)
    {
        foreach (var unit in units.Values.OrderBy(unit => unit.Id, StringComparer.Ordinal).ToArray())
        {
            if (unit.Status == UnitStatus.Destroyed || unit.Strength <= 0)
            {
                continue;
            }

            var trace = SupplyTracer.Trace(regions.Values.ToArray(), startingState.Routes, unit.Side, unit.RegionId);
            if (trace.IsConnected)
            {
                var recovery = 1
                    + regions[unit.RegionId].SupplyValue
                    + CampaignModifierRules.Value(startingState, unit.Side, "supplyDiscipline");
                var nextSupply = Math.Min(10, unit.Supply + Math.Max(1, recovery));
                var nextStatus = nextSupply <= 3 ? UnitSupplyStatus.LowSupply : UnitSupplyStatus.InSupply;
                units[unit.Id] = unit with
                {
                    Supply = nextSupply,
                    SupplyStatus = nextStatus,
                    OutOfSupplyTurns = 0,
                    Status = unit.SupplyStatus == UnitSupplyStatus.OutOfSupply
                        && unit.Status == UnitStatus.Disrupted
                        && nextSupply >= 4
                            ? UnitStatus.Ready
                            : unit.Status
                };

                if (unit.SupplyStatus == UnitSupplyStatus.OutOfSupply)
                {
                    events.Add(new GameEvent(
                        events.Count + 1,
                        GameEventType.Supply,
                        GameEventScope.Unit,
                        unit.Side,
                        unit.RegionId,
                        unit.Id,
                        $"{unit.Name} reconnected to supply from {regions[trace.SourceRegionId!].Name}.",
                        new Dictionary<string, object?>
                        {
                            ["supplyStatus"] = nextStatus.ToString(),
                            ["sourceRegionId"] = trace.SourceRegionId,
                            ["routeSupplyCost"] = trace.Cost,
                            ["supplyRecovered"] = nextSupply - unit.Supply
                        }));
                }
                continue;
            }

            var outOfSupplyTurns = unit.OutOfSupplyTurns + 1;
            var supplyLoss = 2 + CampaignModifierRules.Value(startingState, unit.Side, "supplyRisk");
            var moraleLoss = 1 + CampaignModifierRules.Value(startingState, unit.Side, "moraleRisk");
            var strengthLoss = outOfSupplyTurns >= 2 ? 1 : 0;
            var nextStrength = Math.Max(0, unit.Strength - strengthLoss);
            units[unit.Id] = unit with
            {
                Supply = Math.Max(0, unit.Supply - supplyLoss),
                Morale = Math.Max(0, unit.Morale - moraleLoss),
                Strength = nextStrength,
                SupplyStatus = UnitSupplyStatus.OutOfSupply,
                OutOfSupplyTurns = outOfSupplyTurns,
                Status = nextStrength == 0 ? UnitStatus.Destroyed : UnitStatus.Disrupted,
                IsEntrenched = false
            };
            events.Add(new GameEvent(
                events.Count + 1,
                GameEventType.Supply,
                GameEventScope.Unit,
                unit.Side,
                unit.RegionId,
                unit.Id,
                strengthLoss > 0
                    ? $"{unit.Name} suffered attrition after {outOfSupplyTurns} turns out of supply."
                    : $"{unit.Name} is out of supply.",
                new Dictionary<string, object?>
                {
                    ["supplyStatus"] = UnitSupplyStatus.OutOfSupply.ToString(),
                    ["outOfSupplyTurns"] = outOfSupplyTurns,
                    ["supplyLoss"] = supplyLoss,
                    ["moraleLoss"] = moraleLoss,
                    ["strengthLoss"] = strengthLoss
                }));
        }
    }

    private static void ApplyModifierResourceEffects(
        GameState startingState,
        Dictionary<Side, Resources> resources,
        List<GameEvent> events)
    {
        var manpowerRisk = CampaignModifierRules.Value(startingState, startingState.PlayerSide, "manpowerRisk");
        if (manpowerRisk <= 0 || !resources.TryGetValue(startingState.PlayerSide, out var playerResources))
        {
            return;
        }

        resources[startingState.PlayerSide] = playerResources with
        {
            Manpower = Math.Max(0, playerResources.Manpower - manpowerRisk)
        };
        events.Add(new GameEvent(
            events.Count + 1,
            GameEventType.System,
            GameEventScope.Campaign,
            startingState.PlayerSide,
            null,
            null,
            $"Campaign pressure consumed {manpowerRisk} manpower.",
            new Dictionary<string, object?>
            {
                ["effect"] = "ModifierResourceCost",
                ["resource"] = ResourceType.Manpower.ToString(),
                ["amount"] = manpowerRisk
            }));
    }

    private static (VictoryResult? Result, IReadOnlyDictionary<string, int> Progress) ResolveCampaignResult(GameState state)
    {
        var progress = state.VictoryProgress.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        VictoryResult? result = null;

        foreach (var outcome in state.VictoryRules.Outcomes.OrderBy(outcome => outcome.Priority))
        {
            var outcomeSatisfied = true;
            for (var index = 0; index < outcome.AllOf.Count; index++)
            {
                var condition = outcome.AllOf[index];
                var progressKey = $"{outcome.Id}:{index}";
                var rawSatisfied = EvaluateVictoryCondition(condition, state);
                var count = rawSatisfied ? progress.GetValueOrDefault(progressKey) + 1 : 0;
                progress[progressKey] = count;
                outcomeSatisfied &= count >= Math.Max(1, condition.ConsecutiveTurns);
            }

            if (outcomeSatisfied && result is null)
            {
                result = outcome.Result;
            }
        }

        return (result, progress);
    }

    private static bool EvaluateVictoryCondition(
        VictoryConditionDefinition condition,
        GameState state)
    {
        var side = condition.Side.HasValue ? ResolveSide(condition.Side.Value, state) : (Side?)null;
        return condition.Type switch
        {
            VictoryConditionType.ControlRegion =>
                side.HasValue && condition.RegionId is not null && state.Regions.FirstOrDefault(region => region.Id == condition.RegionId)?.Owner == side,
            VictoryConditionType.ControlAtLeast =>
                side.HasValue && condition.RequiredCount.HasValue && (condition.RegionIds ?? [])
                    .Count(regionId => state.Regions.FirstOrDefault(region => region.Id == regionId)?.Owner == side) >= condition.RequiredCount,
            VictoryConditionType.SupplyConnected =>
                side.HasValue
                && SupplyTracer.HasConnection(
                    state,
                    side.Value,
                    condition.SourceRegionIds ?? [],
                    condition.DestinationRegionIds ?? []),
            VictoryConditionType.VictoryPointsAtLeast =>
                side.HasValue && condition.Threshold.HasValue && state.Regions
                    .Where(region => region.Owner == side)
                    .Sum(region => region.VictoryPoints) >= condition.Threshold,
            VictoryConditionType.TurnNumberAtLeast =>
                condition.TurnNumber.HasValue && state.TurnNumber >= condition.TurnNumber,
            _ => false
        };
    }

    private static Side ResolveSide(VictorySideSelector selector, GameState state) => selector switch
    {
        VictorySideSelector.Player => state.PlayerSide,
        VictorySideSelector.Enemy => state.EnemySide,
        VictorySideSelector.Axis => Side.Axis,
        VictorySideSelector.Allies => Side.Allies,
        _ => Side.Neutral
    };

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
