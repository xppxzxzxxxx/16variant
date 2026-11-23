#requires -Version 7.4
param([switch]$Ui)

$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath $PSScriptRoot
try {
    dotnet restore DrivingSchool.slnx
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    dotnet build DrivingSchool.slnx -c Release --no-restore
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    dotnet test DrivingSchool.Tests -c Release --no-build --logger trx --results-directory TestResults --collect 'XPlat Code Coverage'
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    if ($Ui) {
        dotnet test DrivingSchool.UiTests -c Release --no-build
        if ($LASTEXITCODE) { exit $LASTEXITCODE }
    }
}
finally {
    Pop-Location
}
