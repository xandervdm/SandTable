namespace SandTable.Engine;

public sealed class BasicAiPlanner
{
    public IReadOnlyList<SubmittedCommand> Plan(
        GameState startingState,
        Side aiSide,
        ReserveCatalog? reserveCatalog = null,
        UnitCatalog? unitCatalog = null)
    {
        var regions = startingState.Regions.ToDictionary(region => region.Id, StringComparer.Ordinal);
        var activeUnits = startingState.Units
            .Where(unit => unit.Status != UnitStatus.Destroyed && unit.Strength > 0)
            .ToArray();
        var plans = new List<ScoredPlan>();

        AddReservePlans(startingState, aiSide, reserveCatalog, unitCatalog, regions, plans);
        foreach (var unit in activeUnits.Where(unit => unit.Side == aiSide).OrderBy(unit => unit.Id, StringComparer.Ordinal))
        {
            plans.Add(ScoreUnitPlan(startingState, unit, aiSide, activeUnits, regions));
        }

        CoordinateSupport(startingState, aiSide, activeUnits, regions, plans);
        var available = CommandEconomy.CreateTurnBudget(startingState, aiSide);
        var selected = new List<SubmittedCommand>();
        foreach (var plan in plans
            .OrderByDescending(plan => plan.Score)
            .ThenBy(plan => plan.Command.UnitId, StringComparer.Ordinal)
            .ThenBy(plan => plan.Command.CommandType))
        {
            var command = plan.Command with { Sequence = selected.Count + 1 };
            var cost = CommandEconomy.CalculateCost(startingState, command, reserveCatalog);
            if (CommandEconomy.CanAfford(available, cost))
            {
                available = CommandEconomy.Spend(available, cost);
                selected.Add(command);
            }
            else if (command.UnitId is not null && command.RegionId is not null)
            {
                selected.Add(new SubmittedCommand(
                    selected.Count + 1,
                    CommandSource.AI,
                    aiSide,
                    new HoldPositionCommandPayload(command.UnitId, command.RegionId)));
            }
        }

        return selected;
    }

    private static void AddReservePlans(
        GameState state,
        Side aiSide,
        ReserveCatalog? reserveCatalog,
        UnitCatalog? unitCatalog,
        IReadOnlyDictionary<string, RegionState> regions,
        List<ScoredPlan> plans)
    {
        var deployments = 0;
        foreach (var reserve in state.Reserves
            .Where(candidate => candidate.Side == aiSide && candidate.Status == ReserveStatus.Available)
            .OrderBy(candidate => candidate.ReserveId, StringComparer.Ordinal))
        {
            if (deployments >= state.DeploymentLimitPerSidePerTurn)
            {
                break;
            }
            var definition = reserveCatalog?.Reserves.FirstOrDefault(candidate => candidate.ReserveId == reserve.ReserveId);
            var target = definition?.EligibleRegionIds
                .Select(regionId => regions.GetValueOrDefault(regionId))
                .Where(region => region is not null)
                .OrderByDescending(region => region!.SupplyValue + region.VictoryPoints)
                .ThenBy(region => region!.Id, StringComparer.Ordinal)
                .FirstOrDefault(region => ReserveRules.ValidateDeployment(
                    state,
                    aiSide,
                    new DeployCommandPayload(reserve.ReserveId, region!.Id),
                    reserveCatalog,
                    unitCatalog) is null);
            if (target is null)
            {
                continue;
            }
            plans.Add(new ScoredPlan(
                120 + target.VictoryPoints,
                new SubmittedCommand(0, CommandSource.AI, aiSide, new DeployCommandPayload(reserve.ReserveId, target.Id)),
                "Deploy reserve into a supplied operational position"));
            deployments++;
        }
    }

    private static ScoredPlan ScoreUnitPlan(
        GameState state,
        UnitState unit,
        Side aiSide,
        IReadOnlyCollection<UnitState> activeUnits,
        IReadOnlyDictionary<string, RegionState> regions)
    {
        var current = regions[unit.RegionId];
        var reachable = OperationalPathfinder.FindReachablePaths(state, unit, includeEnemyOccupiedDestinations: true);
        var damaged = unit.Strength * 2 <= unit.MaxStrength || unit.Morale <= 3 || unit.Status == UnitStatus.Disrupted;
        if (unit.SupplyStatus == UnitSupplyStatus.OutOfSupply || unit.Supply <= 2 || damaged)
        {
            var withdrawal = reachable
                .Where(pair => regions[pair.Key].Owner == aiSide
                    && !activeUnits.Any(enemy => enemy.Side != aiSide
                        && enemy.Side != Side.Neutral
                        && enemy.RegionId == pair.Key))
                .Select(pair => new
                {
                    Region = regions[pair.Key],
                    Path = pair.Value,
                    Supplied = SupplyTracer.Trace(state, aiSide, pair.Key).IsConnected
                })
                .OrderByDescending(candidate => candidate.Supplied)
                .ThenByDescending(candidate => candidate.Region.SupplyValue)
                .ThenBy(candidate => candidate.Path.Count)
                .ThenBy(candidate => candidate.Region.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            if (withdrawal is not null && withdrawal.Region.Id != unit.RegionId)
            {
                return new ScoredPlan(
                    110 + (withdrawal.Supplied ? 10 : 0),
                    new SubmittedCommand(0, CommandSource.AI, aiSide,
                        new MoveCommandPayload(unit.Id, unit.RegionId, withdrawal.Path)),
                    "Withdraw a damaged or unsupplied formation");
            }
            if (SupplyTracer.Trace(state, aiSide, unit.RegionId).IsConnected)
            {
                return new ScoredPlan(
                    115,
                    new SubmittedCommand(0, CommandSource.AI, aiSide,
                        new ResupplyCommandPayload(unit.Id, unit.RegionId)),
                    "Restore a vulnerable formation");
            }
            return Hold(unit, aiSide, 105, "Preserve a cut-off formation");
        }

        var attack = reachable
            .Select(pair => new
            {
                Region = regions[pair.Key],
                Path = pair.Value,
                Defenders = activeUnits.Where(enemy => enemy.Side != aiSide
                    && enemy.Side != Side.Neutral
                    && enemy.RegionId == pair.Key).ToArray()
            })
            .Where(candidate => candidate.Defenders.Length > 0)
            .Select(candidate => new
            {
                candidate.Region,
                candidate.Path,
                Score = 60
                    + candidate.Region.VictoryPoints * 5
                    + candidate.Region.SupplyValue * 4
                    + unit.Attack + unit.Strength
                    - candidate.Defenders.Sum(defender => defender.Defence + defender.Strength / 2)
                    + activeUnits.Count(ally => ally.Side == aiSide
                        && ally.Id != unit.Id
                        && regions[ally.RegionId].AdjacentRegionIds.Contains(candidate.Region.Id, StringComparer.Ordinal)) * 5
            })
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Path.Count)
            .ThenBy(candidate => candidate.Region.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (attack is not null)
        {
            return new ScoredPlan(
                attack.Score,
                new SubmittedCommand(0, CommandSource.AI, aiSide,
                    new AttackCommandPayload(unit.Id, unit.RegionId, attack.Path)),
                "Attack a valuable or supply-critical position");
        }

        var threatened = current.VictoryPoints >= 5 && current.AdjacentRegionIds.Any(regionId =>
            activeUnits.Any(enemy => enemy.Side != aiSide && enemy.Side != Side.Neutral && enemy.RegionId == regionId));
        if (threatened)
        {
            return Hold(unit, aiSide, 75 + current.VictoryPoints, "Defend a threatened objective");
        }

        var currentEnemyDistance = DistanceToNearestEnemy(state, unit.RegionId, aiSide, activeUnits);
        var advance = reachable
            .Where(pair => !activeUnits.Any(enemy => enemy.Side != aiSide
                && enemy.Side != Side.Neutral
                && enemy.RegionId == pair.Key))
            .Select(pair => new
            {
                Region = regions[pair.Key],
                Path = pair.Value,
                EnemyDistance = DistanceToNearestEnemy(state, pair.Key, aiSide, activeUnits),
                Score = (regions[pair.Key].Owner == aiSide ? 0 : 30)
                    + regions[pair.Key].VictoryPoints * 4
                    + regions[pair.Key].SupplyValue * 3
                    + Math.Max(0, currentEnemyDistance - DistanceToNearestEnemy(state, pair.Key, aiSide, activeUnits)) * 12
                    - CommandEconomy.CalculatePathMovementCost(state.Routes, unit.RegionId, pair.Value)
            })
            .Where(candidate => candidate.Region.Owner != aiSide || candidate.EnemyDistance < currentEnemyDistance)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Path.Count)
            .ThenBy(candidate => candidate.Region.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (advance is not null && advance.Score > current.VictoryPoints * 4)
        {
            return new ScoredPlan(
                35 + advance.Score,
                new SubmittedCommand(0, CommandSource.AI, aiSide,
                    new MoveCommandPayload(unit.Id, unit.RegionId, advance.Path)),
                "Advance toward an objective or supply junction");
        }

        return Hold(unit, aiSide, 20 + current.VictoryPoints, "Conserve force at the current position");
    }

    private static int DistanceToNearestEnemy(
        GameState state,
        string startRegionId,
        Side aiSide,
        IReadOnlyCollection<UnitState> activeUnits)
    {
        var targets = activeUnits
            .Where(unit => unit.Side != aiSide && unit.Side != Side.Neutral)
            .Select(unit => unit.RegionId)
            .Concat(state.Regions.Where(region => region.Owner != aiSide
                && region.Owner != Side.Neutral
                && region.VictoryPoints > 0).Select(region => region.Id))
            .ToHashSet(StringComparer.Ordinal);
        var distances = new Dictionary<string, int>(StringComparer.Ordinal) { [startRegionId] = 0 };
        var queue = new PriorityQueue<string, int>();
        queue.Enqueue(startRegionId, 0);
        while (queue.TryDequeue(out var current, out var cost))
        {
            if (targets.Contains(current))
            {
                return cost;
            }
            if (distances[current] < cost)
            {
                continue;
            }
            foreach (var route in state.Routes.Where(route => route.FromRegionId == current || route.ToRegionId == current))
            {
                var next = route.FromRegionId == current ? route.ToRegionId : route.FromRegionId;
                var candidate = cost + route.MovementCost;
                if (!distances.TryGetValue(next, out var known) || candidate < known)
                {
                    distances[next] = candidate;
                    queue.Enqueue(next, candidate);
                }
            }
        }
        return 1000;
    }

    private static void CoordinateSupport(
        GameState state,
        Side aiSide,
        IReadOnlyCollection<UnitState> activeUnits,
        IReadOnlyDictionary<string, RegionState> regions,
        List<ScoredPlan> plans)
    {
        var attackTargets = plans
            .Where(plan => plan.Command.Payload is AttackCommandPayload)
            .Select(plan => plan.Command.TargetRegionId)
            .Where(regionId => regionId is not null)
            .Cast<string>()
            .GroupBy(regionId => regionId, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenByDescending(group => regions[group.Key].VictoryPoints)
            .Select(group => group.Key)
            .ToArray();
        if (attackTargets.Length == 0)
        {
            return;
        }

        for (var index = 0; index < plans.Count; index++)
        {
            var plan = plans[index];
            if (plan.Command.UnitId is null || plan.Command.CommandType == OrderType.Attack)
            {
                continue;
            }
            var unit = activeUnits.First(candidate => candidate.Id == plan.Command.UnitId);
            var target = attackTargets.FirstOrDefault(regionId =>
                regions[unit.RegionId].AdjacentRegionIds.Append(unit.RegionId).Contains(regionId, StringComparer.Ordinal));
            if (target is null || unit.Type is not UnitType.Logistics && unit.Attack >= 4)
            {
                continue;
            }
            plans[index] = new ScoredPlan(
                90 + regions[target].VictoryPoints,
                new SubmittedCommand(0, CommandSource.AI, aiSide,
                    new SupportCommandPayload(unit.Id, unit.RegionId, target)),
                "Coordinate support for the main attack");
        }
    }

    private static ScoredPlan Hold(UnitState unit, Side side, int score, string reason) => new(
        score,
        new SubmittedCommand(0, CommandSource.AI, side, new HoldPositionCommandPayload(unit.Id, unit.RegionId)),
        reason);

    private sealed record ScoredPlan(int Score, SubmittedCommand Command, string Reason);
}
