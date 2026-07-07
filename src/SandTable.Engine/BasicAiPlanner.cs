namespace SandTable.Engine;

public sealed class BasicAiPlanner
{
    public IReadOnlyList<SubmittedCommand> Plan(GameState startingState, Side aiSide)
    {
        var regions = startingState.Regions.ToDictionary(region => region.Id, StringComparer.Ordinal);
        var commands = new List<SubmittedCommand>();

        foreach (var unit in startingState.Units
            .Where(unit => unit.Side == aiSide && unit.Status != UnitStatus.Destroyed && unit.Strength > 0)
            .OrderByDescending(unit => unit.Attack))
        {
            if (!regions.TryGetValue(unit.RegionId, out var currentRegion))
            {
                continue;
            }

            var adjacentEnemyUnit = startingState.Units
                .Where(target => target.Side != aiSide
                    && target.Side != Side.Neutral
                    && target.Status != UnitStatus.Destroyed
                    && currentRegion.AdjacentRegionIds.Contains(target.RegionId, StringComparer.Ordinal))
                .OrderBy(target => target.Defence + target.Strength)
                .FirstOrDefault();

            if (adjacentEnemyUnit is not null)
            {
                commands.Add(new SubmittedCommand(commands.Count + 1, CommandSource.AI, aiSide, OrderType.Attack, unit.Id, unit.RegionId, adjacentEnemyUnit.RegionId));
                continue;
            }

            var adjacentEnemyRegion = currentRegion.AdjacentRegionIds
                .Select(id => regions[id])
                .Where(region => region.Owner != aiSide && region.Owner != Side.Neutral)
                .OrderByDescending(region => region.VictoryPoints)
                .FirstOrDefault();

            commands.Add(adjacentEnemyRegion is null
                ? new SubmittedCommand(commands.Count + 1, CommandSource.AI, aiSide, OrderType.HoldPosition, unit.Id, unit.RegionId, null)
                : new SubmittedCommand(commands.Count + 1, CommandSource.AI, aiSide, OrderType.Move, unit.Id, unit.RegionId, adjacentEnemyRegion.Id));
        }

        return commands;
    }
}
