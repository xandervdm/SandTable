namespace SandTable.Engine;

public sealed record PreparedAttack(
    SubmittedCommand Command,
    string UnitId,
    string OriginRegionId,
    string StagingRegionId,
    string TargetRegionId,
    IReadOnlyList<string> PathRegionIds,
    int MovementCost);

public static class RegionalBattleResolver
{
    public static ResolvedCommand PrepareAttack(
        SubmittedCommand command,
        GameState state,
        Dictionary<string, RegionState> regions,
        Dictionary<string, UnitState> units,
        List<PreparedAttack> preparedAttacks)
    {
        if (command.Payload is not AttackCommandPayload attack
            || !units.TryGetValue(attack.UnitIdValue, out var unit)
            || unit.Side != command.Side
            || unit.Status == UnitStatus.Destroyed
            || unit.Strength <= 0)
        {
            return Reject(command, "Attack must reference an active unit belonging to the issuing side.");
        }
        if (!OperationalPathfinder.TryValidatePath(
            state, unit, attack.FromRegionId, attack.PathRegionIds, out var movementCost, out var pathError))
        {
            return Reject(command, pathError ?? "Attack command path is invalid.");
        }

        var targetRegionId = OperationalPathfinder.FirstEnemyContact(units, unit.Side, attack.PathRegionIds)
            ?? attack.PathRegionIds[^1];
        var approachPath = attack.PathRegionIds.TakeWhile(regionId => regionId != targetRegionId).ToArray();
        var stagingRegionId = approachPath.Length > 0 ? approachPath[^1] : unit.RegionId;
        foreach (var regionId in approachPath)
        {
            var region = regions[regionId];
            regions[regionId] = region with { Owner = unit.Side };
        }
        if (stagingRegionId != unit.RegionId)
        {
            units[unit.Id] = unit with
            {
                RegionId = stagingRegionId,
                Supply = Math.Max(0, unit.Supply - Math.Max(1, movementCost / 2)),
                IsEntrenched = false
            };
        }

        preparedAttacks.Add(new PreparedAttack(
            command,
            unit.Id,
            unit.RegionId,
            stagingRegionId,
            targetRegionId,
            attack.PathRegionIds.ToArray(),
            movementCost));
        return new ResolvedCommand(command, Accepted: true, RejectionReason: null, CommandEconomy.Zero);
    }

    public static void Resolve(
        GameState startingState,
        IReadOnlyCollection<PreparedAttack> preparedAttacks,
        Dictionary<string, RegionState> regions,
        Dictionary<string, UnitState> units,
        IReadOnlyDictionary<(Side Side, string RegionId), int> supportByRegion,
        IReadOnlySet<(Side Side, string RegionId)> reconByRegion,
        List<GameEvent> events,
        Random random)
    {
        foreach (var group in preparedAttacks
            .GroupBy(attack => (attack.TargetRegionId, attack.Command.Side))
            .OrderBy(group => group.Key.TargetRegionId, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Side))
        {
            ResolveGroup(startingState, group.ToArray(), regions, units, supportByRegion, reconByRegion, events, random);
        }
    }

    private static void ResolveGroup(
        GameState startingState,
        IReadOnlyList<PreparedAttack> attacks,
        Dictionary<string, RegionState> regions,
        Dictionary<string, UnitState> units,
        IReadOnlyDictionary<(Side Side, string RegionId), int> supportByRegion,
        IReadOnlySet<(Side Side, string RegionId)> reconByRegion,
        List<GameEvent> events,
        Random random)
    {
        var target = regions[attacks[0].TargetRegionId];
        var attackSide = attacks[0].Command.Side;
        var attackers = attacks
            .Select(attack => units.GetValueOrDefault(attack.UnitId))
            .Where(unit => unit is not null && unit.Status != UnitStatus.Destroyed && unit.Strength > 0)
            .Cast<UnitState>()
            .OrderBy(unit => unit.Id, StringComparer.Ordinal)
            .ToArray();
        if (attackers.Length == 0)
        {
            return;
        }

        var defenders = units.Values
            .Where(unit => unit.RegionId == target.Id
                && unit.Side != attackSide
                && unit.Side != Side.Neutral
                && unit.Status != UnitStatus.Destroyed
                && unit.Strength > 0)
            .OrderBy(unit => unit.Id, StringComparer.Ordinal)
            .ToArray();
        if (defenders.Length == 0)
        {
            OccupyWithoutResistance(attacks, attackers, target, attackSide, regions, units, events);
            return;
        }

        var defenceSide = defenders[0].Side;
        var attackerSupport = supportByRegion.GetValueOrDefault((attackSide, target.Id));
        var defenderSupport = supportByRegion.GetValueOrDefault((defenceSide, target.Id));
        var reconBonus = reconByRegion.Contains((attackSide, target.Id))
            ? 1 + CampaignModifierRules.Value(startingState, attackSide, "recon")
            : 0;
        var fortifiedBonus = target.Features.Contains("Fortified", StringComparer.Ordinal) ? defenders.Length : 0;
        var attackScore = attackers.Sum(unit => unit.Attack + unit.Strength / 2 + unit.Experience / 3
                - (unit.SupplyStatus == UnitSupplyStatus.OutOfSupply ? 2 : 0))
            + attackerSupport
            + reconBonus
            + CampaignModifierRules.Value(startingState, attackSide, "attack")
            + random.Next(0, 3);
        var defenceScore = defenders.Sum(unit => unit.Defence + unit.Strength / 2 + unit.Morale / 4
                + (unit.IsEntrenched ? 2 : 0)
                - (unit.SupplyStatus == UnitSupplyStatus.OutOfSupply ? 2 : 0))
            + fortifiedBonus
            + defenderSupport
            + CampaignModifierRules.Value(startingState, defenceSide, "defence")
            + random.Next(0, 3);
        var attackersWon = attackScore > defenceScore;
        var margin = Math.Abs(attackScore - defenceScore);
        var attackerDamage = ApplyBattleDamage(
            units,
            attackers,
            attackersWon ? Math.Max(1, defenders.Length) : Math.Max(2, defenders.Length + margin / 4),
            disruptSurvivors: !attackersWon);
        var defenderDamage = ApplyBattleDamage(
            units,
            defenders,
            attackersWon ? Math.Max(2, attackers.Length + margin / 4) : Math.Max(1, attackers.Length),
            disruptSurvivors: attackersWon);

        string? retreatRegionId = null;
        if (attackersWon && defenders.Any(defender => units[defender.Id].Status != UnitStatus.Destroyed))
        {
            retreatRegionId = SelectRetreatRegion(startingState, defenceSide, target.Id, regions, units);
            if (retreatRegionId is not null)
            {
                foreach (var defender in defenders.Where(defender => units[defender.Id].Status != UnitStatus.Destroyed))
                {
                    var surviving = units[defender.Id];
                    units[defender.Id] = surviving with
                    {
                        RegionId = retreatRegionId,
                        Status = UnitStatus.Disrupted,
                        Morale = Math.Max(0, surviving.Morale - 1),
                        IsEntrenched = false
                    };
                }
            }
        }

        var defendersRemain = defenders.Any(defender =>
            units[defender.Id].Status != UnitStatus.Destroyed && units[defender.Id].RegionId == target.Id);
        var captured = attackersWon && !defendersRemain;
        if (captured)
        {
            foreach (var attacker in attackers.Where(attacker => units[attacker.Id].Status != UnitStatus.Destroyed))
            {
                var surviving = units[attacker.Id];
                units[attacker.Id] = surviving with { RegionId = target.Id, Status = UnitStatus.Ready, IsEntrenched = false };
            }
            regions[target.Id] = target with { Owner = attackSide };
        }

        events.Add(new GameEvent(
            events.Count + 1,
            GameEventType.Battle,
            GameEventScope.Region,
            attackSide,
            target.Id,
            attackers[0].Id,
            attackersWon
                ? $"{attackers.Length} formation(s) broke the defence at {target.Name}."
                : $"{attackers.Length} formation(s) were held at {target.Name}.",
            new Dictionary<string, object?>
            {
                ["attackerUnitId"] = attackers[0].Id,
                ["defenderUnitId"] = defenders[0].Id,
                ["attackerUnitIds"] = attackers.Select(unit => unit.Id).ToArray(),
                ["defenderUnitIds"] = defenders.Select(unit => unit.Id).ToArray(),
                ["fromRegionId"] = attacks[0].OriginRegionId,
                ["toRegionId"] = target.Id,
                ["attackScore"] = attackScore,
                ["defenceScore"] = defenceScore,
                ["attackerSupport"] = attackerSupport,
                ["defenderSupport"] = defenderSupport,
                ["reconBonus"] = reconBonus,
                ["attackerDamage"] = attackerDamage,
                ["defenderDamage"] = defenderDamage,
                ["attackerDestroyed"] = attackers.All(unit => units[unit.Id].Status == UnitStatus.Destroyed),
                ["defenderDestroyed"] = defenders.All(unit => units[unit.Id].Status == UnitStatus.Destroyed),
                ["retreatRegionId"] = retreatRegionId,
                ["previousOwner"] = target.Owner.ToString(),
                ["objectiveCaptured"] = captured && target.Owner != attackSide && target.VictoryPoints > 0
            }));
    }

    private static void OccupyWithoutResistance(
        IReadOnlyList<PreparedAttack> attacks,
        IReadOnlyList<UnitState> attackers,
        RegionState target,
        Side attackSide,
        Dictionary<string, RegionState> regions,
        Dictionary<string, UnitState> units,
        List<GameEvent> events)
    {
        foreach (var attacker in attackers)
        {
            units[attacker.Id] = attacker with { RegionId = target.Id, IsEntrenched = false };
        }
        var previousOwner = target.Owner;
        regions[target.Id] = target with { Owner = attackSide };
        events.Add(new GameEvent(
            events.Count + 1,
            GameEventType.Movement,
            GameEventScope.Region,
            attackSide,
            target.Id,
            attackers[0].Id,
            $"{attackers.Count} formation(s) occupied {target.Name} without resistance.",
            new Dictionary<string, object?>
            {
                ["fromRegionId"] = attacks[0].OriginRegionId,
                ["toRegionId"] = target.Id,
                ["attackerUnitIds"] = attackers.Select(unit => unit.Id).ToArray(),
                ["previousOwner"] = previousOwner.ToString(),
                ["objectiveCaptured"] = previousOwner != attackSide && target.VictoryPoints > 0,
                ["pathRegionIds"] = attacks[0].PathRegionIds
            }));
    }

    private static int ApplyBattleDamage(
        Dictionary<string, UnitState> units,
        IReadOnlyList<UnitState> participants,
        int damage,
        bool disruptSurvivors)
    {
        var applied = 0;
        var ordered = participants.OrderBy(unit => unit.Morale).ThenBy(unit => unit.Id, StringComparer.Ordinal).ToArray();
        for (var point = 0; point < damage; point++)
        {
            var candidates = ordered.Where(unit => units[unit.Id].Strength > 0).ToArray();
            if (candidates.Length == 0)
            {
                break;
            }
            var target = candidates[point % candidates.Length];
            var current = units[target.Id];
            var strength = Math.Max(0, current.Strength - 1);
            units[target.Id] = current with
            {
                Strength = strength,
                Morale = Math.Max(0, current.Morale - (disruptSurvivors ? 1 : 0)),
                Status = strength == 0 ? UnitStatus.Destroyed : disruptSurvivors ? UnitStatus.Disrupted : current.Status,
                IsEntrenched = strength > 0 && current.IsEntrenched
            };
            applied++;
        }
        return applied;
    }

    private static string? SelectRetreatRegion(
        GameState startingState,
        Side side,
        string fromRegionId,
        IReadOnlyDictionary<string, RegionState> regions,
        IReadOnlyDictionary<string, UnitState> units)
    {
        var liveState = startingState with
        {
            Regions = regions.Values.ToArray(),
            Units = units.Values.ToArray()
        };
        return regions[fromRegionId].AdjacentRegionIds
            .Select(id => regions[id])
            .Where(region => region.Owner == side && !units.Values.Any(unit =>
                unit.RegionId == region.Id
                && unit.Side != side
                && unit.Side != Side.Neutral
                && unit.Status != UnitStatus.Destroyed))
            .OrderByDescending(region => SupplyTracer.Trace(liveState, side, region.Id).IsConnected)
            .ThenByDescending(region => region.SupplyValue)
            .ThenByDescending(region => region.VictoryPoints)
            .ThenBy(region => region.Id, StringComparer.Ordinal)
            .Select(region => region.Id)
            .FirstOrDefault();
    }

    private static ResolvedCommand Reject(SubmittedCommand command, string reason) =>
        new(command, Accepted: false, RejectionReason: reason, CommandEconomy.Zero);
}
