#requires -Version 7.4
param([switch]$Ui, [switch]$Legacy)

$ErrorActionPreference = 'Stop'
[string[]]$properties = if ($Legacy) { @('-p:SchoolLegacy=true') } else { @() }
Push-Location -LiteralPath $PSScriptRoot
try {
    dotnet restore DrivingSchool.sln @properties
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    dotnet build DrivingSchool.sln -c Release --no-restore @properties
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    dotnet test DrivingSchool.Tests -c Release --no-build @properties --logger trx --results-directory TestResults --collect 'XPlat Code Coverage'
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    if ($Ui) {
        dotnet test DrivingSchool.UiTests -c Release --no-build @properties
        if ($LASTEXITCODE) { exit $LASTEXITCODE }
    }
}
finally {
    Pop-Location
}
