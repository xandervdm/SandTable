$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$MainSqlPath = Join-Path $ScriptDir "main.sql"

$DatabaseUrlVariableName = "VULTR_POSTGRES_URL_SAND_TABLE_DEV"
$AtlasDevUrlVariableName = "POSTGRES_ATLAS_DEV"

$DatabaseUrl = [System.Environment]::GetEnvironmentVariable($DatabaseUrlVariableName)
$AtlasDevUrl = [System.Environment]::GetEnvironmentVariable($AtlasDevUrlVariableName)

if ([string]::IsNullOrWhiteSpace($DatabaseUrl)) {
    throw "$DatabaseUrlVariableName is not set."
}

if ([string]::IsNullOrWhiteSpace($AtlasDevUrl)) {
    throw "$AtlasDevUrlVariableName is not set."
}

if (-not (Test-Path -LiteralPath $MainSqlPath)) {
    throw "main.sql was not found. Run ./db-refresh-main.ps1 first."
}

Push-Location $ScriptDir
try {
    atlas schema apply `
      --url "$DatabaseUrl" `
      --to "file://main.sql" `
      --dev-url "$AtlasDevUrl" `
      --auto-approve
}
finally {
    Pop-Location
}
