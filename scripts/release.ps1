[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
    [switch]$Push
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$props = Join-Path $root 'Directory.Build.props'
$package = Join-Path $root 'client/vscode/package.json'
$changelog = Join-Path $root 'client/vscode/CHANGELOG.md'

Push-Location $root
try {
    $isRepository = (git rev-parse --is-inside-work-tree 2>$null) -eq 'true'
    if ($isRepository -and (git status --porcelain)) {
        throw 'The working tree has uncommitted changes. Commit or stash them first.'
    }

    $notes = Get-Content $changelog -Raw
    if ($notes -match "(?m)^## $([regex]::Escape($Version))\s*$") {
        Write-Host "CHANGELOG.md already contains a section for $Version."
    }
    elseif ($notes -match '(?m)^## Unreleased\s*$') {
        $notes = $notes -replace '(?m)^## Unreleased\s*$', "## $Version"
    }
    else {
        throw "CHANGELOG.md needs a '## Unreleased' or '## $Version' section describing the release."
    }

    if ($PSCmdlet.ShouldProcess($root, "Set version $Version")) {
        [System.IO.File]::WriteAllText($changelog, $notes)

        $propsText = Get-Content $props -Raw
        $propsText = $propsText -replace '<Version>[^<]*</Version>', "<Version>$Version</Version>"
        [System.IO.File]::WriteAllText($props, $propsText)

        Push-Location (Split-Path $package)
        try {
            npm version $Version --no-git-tag-version --allow-same-version | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'npm version failed.' }
        }
        finally {
            Pop-Location
        }

        if ($isRepository) {
            git add $props $package (Join-Path $root 'client/vscode/package-lock.json') $changelog
            git commit -m "Release v$Version"
            git tag -a "v$Version" -m "v$Version"
            if ($Push) {
                git push --follow-tags
                Write-Host "Pushed v$Version. The release workflow builds and publishes the packages."
            }
            else {
                Write-Host "Tagged v$Version. Run 'git push --follow-tags' to start the release workflow."
            }
        }
        else {
            Write-Host "Updated the version to $Version. Initialize a Git repository and push a 'v$Version' tag to start the release workflow."
        }
    }
}
finally {
    Pop-Location
}
