using System.Text.Json;
using System.Text.Json.Serialization;

namespace SandTable.Engine.Tests;

public class StrategicTensionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Generator_uses_catalog_caps_cards_and_avoids_duplicates()
    {
        var content = await LoadContentAsync();
        var state = new Engine.ScenarioFactory().CreateInitialState(content.Map, content.Scenario, content.Units);
        var generator = new Engine.BasicTensionGenerator();

        var firstCard = generator.Generate(state, content.TensionCards, randomSeed: 1942, maxCards: 1);
        var nextState = state with { ActiveTensions = firstCard };
        var generated = generator.Generate(nextState, content.TensionCards, randomSeed: 1942, maxCards: 2);

        Assert.Single(firstCard);
        Assert.True(generated.Count <= 1);
        Assert.DoesNotContain(generated, card => card.Id == firstCard[0].Id);
        Assert.Equal(generated.Select(card => card.Id).Distinct(StringComparer.Ordinal).Count(), generated.Count);
        Assert.All(generated, card => Assert.Contains(content.TensionCards.Cards, definition => definition.Id == card.Id));
    }

    [Fact]
    public async Task Generator_is_deterministic_for_same_state_and_seed()
    {
        var content = await LoadContentAsync();
        var state = new Engine.ScenarioFactory().CreateInitialState(content.Map, content.Scenario, content.Units);
        var generator = new Engine.BasicTensionGenerator();

        var first = generator.Generate(state, content.TensionCards, randomSeed: 500, maxCards: 2);
        var second = generator.Generate(state, content.TensionCards, randomSeed: 500, maxCards: 2);

        Assert.Equal(first.Select(card => card.Id), second.Select(card => card.Id));
        Assert.Equal(first.Select(card => card.Options[0].Effects[0].Description), second.Select(card => card.Options[0].Effects[0].Description));
    }

    [Fact]
    public async Task Choosing_valid_option_applies_effects_records_history_and_removes_card()
    {
        var content = await LoadContentAsync();
        var state = new Engine.ScenarioFactory().CreateInitialState(content.Map, content.Scenario, content.Units);
        var card = new Engine.BasicTensionGenerator()
            .Generate(state, content.TensionCards, randomSeed: 1942, maxCards: 2)
            .First();
        var activeState = state with { ActiveTensions = new[] { card } };
        var option = card.Options[0];

        var result = new Engine.TensionChoiceResolver()
            .Choose(activeState, new Engine.ChooseTensionOptionCommand(card.Id, option.Id, state.PlayerSide));

        Assert.Empty(result.State.ActiveTensions);
        Assert.Single(result.State.TensionHistory);
        Assert.Equal(card.Id, result.Decision.CardId);
        Assert.Equal(option.Id, result.Decision.OptionId);
        Assert.NotEmpty(result.Events);
        Assert.NotEqual(JsonSerializer.Serialize(activeState, JsonOptions), JsonSerializer.Serialize(result.State, JsonOptions));
    }

    [Fact]
    public async Task Choosing_invalid_card_or_option_is_rejected()
    {
        var content = await LoadContentAsync();
        var state = new Engine.ScenarioFactory().CreateInitialState(content.Map, content.Scenario, content.Units);
        var card = new Engine.BasicTensionGenerator()
            .Generate(state, content.TensionCards, randomSeed: 1942, maxCards: 2)
            .First();
        var activeState = state with { ActiveTensions = new[] { card } };
        var resolver = new Engine.TensionChoiceResolver();

        Assert.Throws<Engine.TensionChoiceValidationException>(() =>
            resolver.Choose(activeState, new Engine.ChooseTensionOptionCommand("missing-card", card.Options[0].Id, state.PlayerSide)));
        Assert.Throws<Engine.TensionChoiceValidationException>(() =>
            resolver.Choose(activeState, new Engine.ChooseTensionOptionCommand(card.Id, "missing-option", state.PlayerSide)));
    }

    [Fact]
    public async Task Effect_application_is_deterministic()
    {
        var content = await LoadContentAsync();
        var state = new Engine.ScenarioFactory().CreateInitialState(content.Map, content.Scenario, content.Units);
        var effects = new Engine.GameEffect[]
        {
            new Engine.AddResourceEffect(Engine.ResourceType.Fuel, 3, "Fuel increases by 3."),
            new Engine.ModifyUnitStatEffect("15th-panzer", Engine.UnitStat.Supply, -2, "15th Panzer Division loses 2 supply.")
        };

        var result = new Engine.GameEffectApplier().Apply(state, effects, startingEventSequence: 1);

        Assert.Equal(state.Resources.Fuel + 3, result.State.Resources.Fuel);
        Assert.Equal(6, result.State.Units.Single(unit => unit.Id == "15th-panzer").Supply);
        Assert.Empty(result.Events);
    }

    [Fact]
    public async Task Game_state_with_tensions_round_trips_as_json()
    {
        var content = await LoadContentAsync();
        var state = new Engine.ScenarioFactory().CreateInitialState(content.Map, content.Scenario, content.Units);
        var cards = new Engine.BasicTensionGenerator().Generate(state, content.TensionCards, randomSeed: 1942, maxCards: 2);
        var stateWithTensions = state with { ActiveTensions = cards };

        var json = JsonSerializer.Serialize(stateWithTensions, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<Engine.GameState>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(cards.Select(card => card.Id), deserialized!.ActiveTensions.Select(card => card.Id));
        Assert.IsAssignableFrom<Engine.GameEffect>(deserialized.ActiveTensions[0].Options[0].Effects[0]);
    }

    [Fact]
    public async Task Turn_resolution_generates_catalog_tensions()
    {
        var content = await LoadContentAsync();
        var startingState = new Engine.ScenarioFactory().CreateInitialState(content.Map, content.Scenario, content.Units);
        var aiCommands = new Engine.BasicAiPlanner().Plan(startingState, Engine.Side.Allies);

        var resolution = new Engine.TurnResolver().Resolve(
            startingState,
            Array.Empty<Engine.SubmittedCommand>(),
            aiCommands,
            randomSeed: 1942,
            content.TensionCards);

        Assert.InRange(resolution.NextState.ActiveTensions.Count, 1, 2);
        Assert.Contains(resolution.Events, gameEvent => gameEvent.Summary.StartsWith("Operational opportunity emerged:", StringComparison.Ordinal));
    }

    private static async Task<(Engine.MapDefinition Map, Engine.ScenarioDefinition Scenario, Engine.UnitCatalog Units, Engine.TensionCardCatalog TensionCards)> LoadContentAsync()
    {
        var theatrePath = Path.Combine(FindRepoRoot(), "content", "theatres", "north-africa");
        return (
            await ReadJsonAsync<Engine.MapDefinition>(Path.Combine(theatrePath, "map.json")),
            await ReadJsonAsync<Engine.ScenarioDefinition>(Path.Combine(theatrePath, "scenario-1942.json")),
            await ReadJsonAsync<Engine.UnitCatalog>(Path.Combine(theatrePath, "units.json")),
            await ReadJsonAsync<Engine.TensionCardCatalog>(Path.Combine(theatrePath, "tension-cards.json")));
    }

    private static async Task<T> ReadJsonAsync<T>(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Could not read {path}.");
    }

    private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
    {
        var sourceDirectory = Path.GetDirectoryName(sourceFilePath);
        foreach (var candidate in new[] { sourceDirectory, Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var current = new DirectoryInfo(candidate);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "SandTable.slnx")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate SandTable.slnx.");
    }
}
