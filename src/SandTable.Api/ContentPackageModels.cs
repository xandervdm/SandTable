using SandTable.Engine;

namespace SandTable.Api;

public sealed record TheatreManifest(
    string ContractVersion,
    string TheatreId,
    string Name,
    string DefaultScenarioId,
    TheatreFileReferences Files,
    IReadOnlyList<TheatreScenarioReference> Scenarios);

public sealed record TheatreFileReferences(
    string Map,
    string Display,
    string Assets,
    string Units,
    string Reserves,
    string Doctrines,
    string Events,
    string TensionCards);

public sealed record TheatreScenarioReference(string ScenarioId, string File);

public sealed record TheatreMetadataResponse(
    string ContractVersion,
    string TheatreId,
    string Name,
    string DefaultScenarioId);

public sealed record MapDisplayContent(
    string TheatreId,
    CoordinateSystem CoordinateSystem,
    MapDisplayBackgroundContent BackgroundImage,
    IReadOnlyDictionary<string, RegionDisplayDefinition> Regions);

public sealed record MapDisplayBackgroundContent(string AssetId, string Fit);

public sealed record MapAssetCatalog(IReadOnlyList<MapAssetDefinition> Assets);

public sealed record MapAssetDefinition(
    string AssetId,
    string File,
    string? Origin,
    string? Source,
    DateOnly? CreatedDate,
    string? License,
    string? Attribution,
    string? IntendedUse);

public sealed record MapAssetCatalogResponse(IReadOnlyList<MapAssetResponse> Assets);

public sealed record MapAssetResponse(
    string AssetId,
    string File,
    string Url,
    string? Origin,
    string? Source,
    DateOnly? CreatedDate,
    string? License,
    string? Attribution,
    string? IntendedUse);

internal sealed record LoadedTheatrePackage(
    string TheatrePath,
    TheatreManifest Manifest,
    MapDefinition Map,
    MapDisplayContent Display,
    MapAssetCatalog Assets,
    UnitCatalog Units,
    ReserveCatalog Reserves,
    DoctrineCatalog Doctrines,
    ScenarioEventCatalog Events,
    TensionCardCatalog TensionCards,
    IReadOnlyDictionary<string, ScenarioDefinition> Scenarios);
