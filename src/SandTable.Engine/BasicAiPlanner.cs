namespace SandTable.Engine;

public sealed class BasicAiPlanner
{
    public IReadOnlyList<SubmittedCommand> Plan(GameState startingState, Side aiSide)
    {
        var regions = startingState.Regions.ToDictionary(region => region.Id, StringComparer.Ordinal);
        var activeUnits = startingState.Units
            .Where(unit => unit.Status != UnitStatus.Destroyed && unit.Strength > 0)
            .ToArray();
        var commands = new List<SubmittedCommand>();

        foreach (var unit in activeUnits
            .Where(unit => unit.Side == aiSide)
            .OrderByDescending(unit => unit.Attack))
        {
            if (!regions.TryGetValue(unit.RegionId, out var currentRegion))
            {
                continue;
            }

            if (unit.SupplyStatus == UnitSupplyStatus.OutOfSupply || unit.Supply <= 2)
            {
                commands.Add(SupplyTracer.Trace(startingState, aiSide, unit.RegionId).IsConnected
                    ? new SubmittedCommand(
                        commands.Count + 1,
                        CommandSource.AI,
                        aiSide,
                        new ResupplyCommandPayload(unit.Id, unit.RegionId))
                    : new SubmittedCommand(
                        commands.Count + 1,
                        CommandSource.AI,
                        aiSide,
                        new HoldPositionCommandPayload(unit.Id, unit.RegionId)));
                continue;
            }

            var adjacentEnemyUnit = activeUnits
                .Where(target => target.Side != aiSide
                    && target.Side != Side.Neutral
                    && currentRegion.AdjacentRegionIds.Contains(target.RegionId, StringComparer.Ordinal))
                .OrderBy(target => target.Defence + target.Strength)
                .FirstOrDefault();

            if (adjacentEnemyUnit is not null)
            {
                commands.Add(new SubmittedCommand(
                    commands.Count + 1,
                    CommandSource.AI,
                    aiSide,
                    new AttackCommandPayload(unit.Id, unit.RegionId, [adjacentEnemyUnit.RegionId])));
                continue;
            }

            var adjacentEnemyRegion = currentRegion.AdjacentRegionIds
                .Select(id => regions[id])
                .Where(region => region.Owner != aiSide && region.Owner != Side.Neutral)
                .OrderByDescending(region => region.VictoryPoints)
                .FirstOrDefault();

            if (adjacentEnemyRegion is not null)
            {
                commands.Add(new SubmittedCommand(
                    commands.Count + 1,
                    CommandSource.AI,
                    aiSide,
                    new MoveCommandPayload(unit.Id, unit.RegionId, [adjacentEnemyRegion.Id])));
                continue;
            }

            var nextRegionId = FindNextStepTowardEnemy(currentRegion, regions, activeUnits, aiSide);
            commands.Add(nextRegionId is null
                ? new SubmittedCommand(
                    commands.Count + 1,
                    CommandSource.AI,
                    aiSide,
                    new HoldPositionCommandPayload(unit.Id, unit.RegionId))
                : new SubmittedCommand(
                    commands.Count + 1,
                    CommandSource.AI,
                    aiSide,
                    new MoveCommandPayload(unit.Id, unit.RegionId, [nextRegionId])));
        }

        var available = CommandEconomy.CreateTurnBudget(startingState, aiSide);
        var affordableCommands = new List<SubmittedCommand>();
        foreach (var command in commands)
        {
            var resequenced = command with { Sequence = affordableCommands.Count + 1 };
            var cost = CommandEconomy.CalculateCost(startingState, resequenced);
            if (CommandEconomy.CanAfford(available, cost))
            {
                available = CommandEconomy.Spend(available, cost);
                affordableCommands.Add(resequenced);
            }
            else if (command.UnitId is not null && command.RegionId is not null)
            {
                affordableCommands.Add(new SubmittedCommand(
                    affordableCommands.Count + 1,
                    CommandSource.AI,
                    aiSide,
                    new HoldPositionCommandPayload(command.UnitId, command.RegionId)));
            }
        }

        return affordableCommands;
    }

    private static string? FindNextStepTowardEnemy(
        RegionState start,
        IReadOnlyDictionary<string, RegionState> regions,
        IReadOnlyCollection<UnitState> activeUnits,
        Side aiSide)
    {
        var enemyRegionIds = activeUnits
            .Where(unit => unit.Side != aiSide && unit.Side != Side.Neutral)
            .Select(unit => unit.RegionId)
            .Concat(regions.Values
                .Where(region => region.Owner != aiSide && region.Owner != Side.Neutral)
                .Select(region => region.Id))
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        if (enemyRegionIds.Count == 0)
        {
            return null;
        }

        var enemyOccupiedRegionIds = activeUnits
            .Where(unit => unit.Side != aiSide && unit.Side != Side.Neutral)
            .Select(unit => unit.RegionId)
            .ToHashSet(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal) { start.Id };
        var queue = new Queue<(string RegionId, string FirstStep, int Distance)>();

        foreach (var adjacentRegionId in start.AdjacentRegionIds.OrderBy(regionId => regionId, StringComparer.Ordinal))
        {
            if (!regions.ContainsKey(adjacentRegionId) || !visited.Add(adjacentRegionId))
            {
                continue;
            }

            queue.Enqueue((adjacentRegionId, adjacentRegionId, 1));
        }

        string? selectedStep = null;
        var selectedDistance = int.MaxValue;
        var selectedVictoryPoints = int.MinValue;

        while (queue.Count > 0)
        {
            var candidate = queue.Dequeue();
            var candidateRegion = regions[candidate.RegionId];

            if (enemyRegionIds.Contains(candidate.RegionId))
            {
                if (candidate.Distance < selectedDistance
                    || (candidate.Distance == selectedDistance && candidateRegion.VictoryPoints > selectedVictoryPoints)
                    || (candidate.Distance == selectedDistance
                        && candidateRegion.VictoryPoints == selectedVictoryPoints
                        && string.CompareOrdinal(candidate.FirstStep, selectedStep) < 0))
                {
                    selectedStep = candidate.FirstStep;
                    selectedDistance = candidate.Distance;
                    selectedVictoryPoints = candidateRegion.VictoryPoints;
                }

                continue;
            }

            if (candidate.Distance >= selectedDistance)
            {
                continue;
            }

            foreach (var adjacentRegionId in candidateRegion.AdjacentRegionIds.OrderBy(regionId => regionId, StringComparer.Ordinal))
            {
                if (!regions.ContainsKey(adjacentRegionId) || !visited.Add(adjacentRegionId))
                {
                    continue;
                }

                queue.Enqueue((adjacentRegionId, candidate.FirstStep, candidate.Distance + 1));
            }
        }

        return selectedStep is not null && !enemyOccupiedRegionIds.Contains(selectedStep)
            ? selectedStep
            : null;
    }
}
