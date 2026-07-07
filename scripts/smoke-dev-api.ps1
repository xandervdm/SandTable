[CmdletBinding()]
param(
    [int] $Port = 5171,
    [switch] $SkipTensionChoice
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($env:VULTR_POSTGRES_URL_SAND_TABLE_DEV)) {
    throw 'Set VULTR_POSTGRES_URL_SAND_TABLE_DEV before running the dev API smoke test.'
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$artifacts = Join-Path $env:TEMP ('SandTableSmokeArtifacts-' + [guid]::NewGuid().ToString('N'))
$stdout = Join-Path $env:TEMP ('SandTableApi-' + [guid]::NewGuid().ToString('N') + '.out.log')
$stderr = Join-Path $env:TEMP ('SandTableApi-' + [guid]::NewGuid().ToString('N') + '.err.log')
$baseUrl = "http://127.0.0.1:$Port"
$apiProcess = $null

function Invoke-SmokeJson {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Method,

        [Parameter(Mandatory = $true)]
        [string] $Path,

        [object] $Body = $null
    )

    $parameters = @{
        Method = $Method
        Uri = "$baseUrl$Path"
        Headers = @{ Accept = 'application/json' }
    }

    if ($null -ne $Body) {
        $parameters.ContentType = 'application/json'
        $parameters.Body = ($Body | ConvertTo-Json -Depth 20)
    }

    Invoke-RestMethod @parameters
}

try {
    Push-Location $repoRoot

    $env:DOTNET_CLI_HOME = Join-Path $repoRoot '.dotnet_cli_home_build'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
    $env:DOTNET_NOLOGO = '1'

    dotnet build SandTable.slnx --artifacts-path $artifacts /p:UseSharedCompilation=false

    $apiDll = Join-Path $artifacts 'bin\SandTable.Api\debug\SandTable.Api.dll'
    if (-not (Test-Path $apiDll)) {
        throw "Could not find built API assembly at $apiDll."
    }

    $apiProcess = Start-Process `
        -FilePath 'dotnet' `
        -ArgumentList @($apiDll, '--urls', $baseUrl) `
        -WorkingDirectory $repoRoot `
        -WindowStyle Hidden `
        -RedirectStandardOutput $stdout `
        -RedirectStandardError $stderr `
        -PassThru

    $healthy = $false
    foreach ($attempt in 1..30) {
        try {
            $health = Invoke-SmokeJson -Method Get -Path '/api/health'
            if ($health.status -eq 'Healthy') {
                $healthy = $true
                break
            }
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }

    if (-not $healthy) {
        throw "API did not become healthy on $baseUrl. See $stdout and $stderr."
    }

    $campaign = Invoke-SmokeJson -Method Post -Path '/api/campaigns' -Body @{
        name = 'Smoke Test North Africa Campaign'
        scenarioId = 'north-africa-1942'
        playerSide = 'Axis'
        randomSeed = 1942
    }

    $campaignUid = $campaign.campaign.campaignUid
    if ([string]::IsNullOrWhiteSpace($campaignUid)) {
        throw 'Create campaign response did not include campaign.campaignUid.'
    }

    Invoke-SmokeJson -Method Post -Path "/api/campaigns/$campaignUid/commands" -Body @{
        commands = @(
            @{
                commandType = 'Move'
                unitId = '21st-panzer'
                targetRegionId = 'gazala'
            }
        )
    } | Out-Null

    $resolved = Invoke-SmokeJson -Method Post -Path "/api/campaigns/$campaignUid/resolve-turn"
    $snapshot = Invoke-SmokeJson -Method Get -Path "/api/campaigns/$campaignUid/snapshot"

    $chosenTension = $null
    if (-not $SkipTensionChoice -and $snapshot.state.activeTensions.Count -gt 0) {
        $card = $snapshot.state.activeTensions[0]
        $option = $card.options[0]
        $chosenTension = Invoke-SmokeJson -Method Post -Path "/api/campaigns/$campaignUid/tensions/$($card.id)/choose" -Body @{
            optionId = $option.id
        }
    }

    [pscustomobject]@{
        BaseUrl = $baseUrl
        CampaignUid = $campaignUid
        ResolvedTurn = $resolved.resolvedTurnNumber
        NextTurn = $resolved.nextTurnNumber
        EventCount = $resolved.events.Count
        ActiveTensionCount = $snapshot.state.activeTensions.Count
        ChosenTensionCardId = if ($null -eq $chosenTension) { $null } else { $chosenTension.decision.cardId }
    } | Format-List
}
finally {
    if ($null -ne $apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id
    }

    Pop-Location
}
