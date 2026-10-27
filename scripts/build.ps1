[CmdletBinding()]
param(
    [string]$Runtime,
    [switch]$SkipTests,
    [switch]$IntegrationTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$client = Join-Path $root 'client/vscode'

if (-not $Runtime) {
    $os = if ($IsWindows -or $env:OS -eq 'Windows_NT') { 'win' } elseif ($IsMacOS) { 'osx' } else { 'linux' }
    $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    $Runtime = "$os-$arch"
}

$target = switch -Regex ($Runtime) {
    '^win-(.+)$' { "win32-$($Matches[1])" }
    '^osx-(.+)$' { "darwin-$($Matches[1])" }
    default { $Runtime }
}

if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    $installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
    if ((Test-Path $installer) -and -not ($env:PATH -split ';' | Where-Object { $_ -eq $installer })) {
        $env:PATH = "$installer;$env:PATH"
    }
}

Push-Location $root
try {
    if (-not $SkipTests) {
        dotnet test OMSIScript.slnx -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    }

    $output = Join-Path $root "artifacts/server/$Runtime"
    if (Test-Path $output) { Remove-Item $output -Recurse -Force }
    dotnet publish src/OMSIScript.LanguageServer -c Release -r $Runtime -o $output -p:DebugType=none -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw 'Publishing the language server failed.' }

    $serverFolder = Join-Path $client 'server'
    if (Test-Path $serverFolder) { Remove-Item $serverFolder -Recurse -Force }
    New-Item -ItemType Directory -Path $serverFolder | Out-Null
    Get-ChildItem $output -File | Where-Object { $_.BaseName -eq 'omsi-script-language-server' -and $_.Extension -in '', '.exe' } |
        Copy-Item -Destination $serverFolder

    Push-Location $client
    try {
        npm ci
        if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' }
        if ($IntegrationTests) {
            npm test
            if ($LASTEXITCODE -ne 0) { throw 'Integration tests failed.' }
        }

        $version = (Get-Content package.json -Raw | ConvertFrom-Json).version
        $vsix = Join-Path $root "artifacts/osc-ext-$target-$version.vsix"
        npx vsce package --target $target -o $vsix
        if ($LASTEXITCODE -ne 0) { throw 'Packaging failed.' }
        Write-Host "Created $vsix"
    }
    finally {
        Pop-Location
    }
}
finally {
    Pop-Location
}
