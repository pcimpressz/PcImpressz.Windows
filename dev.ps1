param(
    [ValidateSet('Build', 'Test', 'Status', 'Run', 'Core', 'Publish', 'Setup')]
    [string]$Action = 'Status'
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $localDotnet)) {
    $localDotnet = Join-Path (Split-Path $projectRoot -Parent) '.tools\dotnet\dotnet.exe'
}
$dotnetPath = $null
if (Test-Path -LiteralPath $localDotnet) {
    $dotnetPath = $localDotnet
    $env:DOTNET_ROOT = Split-Path $dotnetPath -Parent
} elseif (Get-Command dotnet -ErrorAction SilentlyContinue) {
    $dotnetPath = (Get-Command dotnet).Source
}
if (-not $dotnetPath) { throw 'A .NET 10 SDK nincs telepitve. Lasd: README.md.' }
$env:DOTNET_HOST_PATH = $dotnetPath
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot 'artifacts\sdk-home'
$env:NUGET_PACKAGES = Join-Path $projectRoot 'artifacts\packages'

Push-Location $projectRoot
try {
    & $dotnetPath restore '.\PcImpressz.Windows.slnx' --configfile '.\NuGet.Config' --locked-mode -p:PcImpresszIntermediate=codex --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $dotnetPath build '.\PcImpressz.Windows.slnx' --configuration Release --no-restore -p:PcImpresszIntermediate=codex --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    switch ($Action) {
        'Build' { exit 0 }
        'Test' {
            & $dotnetPath '.\tests\PcImpressz.Core.Tests\bin\Release\net10.0\PcImpressz.Core.Tests.dll'
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
            & '.\tests\PcImpressz.Shell.WheelTests\bin\Release\net10.0-windows\PcImpressz.Shell.WheelTests.exe'
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
            & '.\tests\PcImpressz.Shell.UiTests\bin\Release\net10.0-windows10.0.19041.0\PcImpressz.Shell.UiTests.exe'
        }
        'Publish' {
            & $dotnetPath publish '.\src\PcImpressz.Shell.Windows\PcImpressz.Shell.Windows.csproj' `
                --configuration Release --no-restore -p:PcImpresszIntermediate=codex --no-self-contained --output '.\artifacts\desktop' --nologo
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
            & $dotnetPath publish '.\src\PcImpressz.ApplicationHost\PcImpressz.ApplicationHost.csproj' `
                --configuration Release --no-restore -p:PcImpresszIntermediate=codex --no-self-contained --output '.\artifacts\desktop\host' --nologo
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
            & $dotnetPath publish '.\tools\PcImpressz.Maintenance\PcImpressz.Maintenance.csproj' `
                --configuration Release --no-restore -p:PcImpresszIntermediate=codex --no-self-contained --output '.\artifacts\desktop\maintenance' --nologo
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
            $noticesOutput = Join-Path $projectRoot 'artifacts\desktop\third-party'
            New-Item -ItemType Directory -Path $noticesOutput -Force | Out-Null
            Get-ChildItem -LiteralPath (Join-Path $projectRoot 'docs\third-party') | Copy-Item -Destination $noticesOutput -Recurse -Force
        }
        'Status' {
            & $dotnetPath '.\src\PcImpressz.ApplicationHost\bin\Release\net10.0\PcImpressz.ApplicationHost.dll' `
                --data-dir (Join-Path $projectRoot '.local') --status
        }
        'Setup' {
            & $dotnetPath '.\tools\PcImpressz.DevelopmentIssuer\bin\Release\net10.0-windows\PcImpressz.DevelopmentIssuer.dll' $projectRoot
        }
        'Run' {
            if (Test-Path -LiteralPath (Join-Path $projectRoot '.issuer')) {
                & $dotnetPath '.\tools\PcImpressz.DevelopmentIssuer\bin\Release\net10.0-windows\PcImpressz.DevelopmentIssuer.dll' $projectRoot
                if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
            }
            $shellPath = Join-Path $projectRoot 'src\PcImpressz.Shell.Windows\bin\Release\net10.0-windows10.0.19041.0\PcImpressz.Windows.exe'
            $hostPath = Join-Path $projectRoot 'src\PcImpressz.ApplicationHost\bin\Release\net10.0\PcImpressz.ApplicationHost.dll'
            Start-Process -FilePath $shellPath -WindowStyle Normal -ArgumentList @(
                '--data-dir', ('"' + (Join-Path $projectRoot '.local') + '"'), '--host', ('"' + $hostPath + '"'))
            exit 0
        }
        'Core' {
            & $dotnetPath '.\src\PcImpressz.ApplicationHost\bin\Release\net10.0\PcImpressz.ApplicationHost.dll' `
                --data-dir (Join-Path $projectRoot '.local')
        }
    }
    exit $LASTEXITCODE
} finally {
    Pop-Location
}
