namespace SandTable.Engine;

public static class CampaignModifierRules
{
    public static int Value(GameState state, Side side, string key)
    {
        if (side != state.PlayerSide)
        {
            return 0;
        }

        return state.CampaignModifiers
            .Where(modifier => modifier.RemainingTurns > 0)
            .Sum(modifier => modifier.Values.TryGetValue(key, out var value) ? value : 0);
    }
}
