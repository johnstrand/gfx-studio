<#
.SYNOPSIS
    Generates GfxStudio GitHub Wiki Markdown pages from registered layer and effect metadata,
    and optionally publishes them to the GitHub project Wiki via `gh` CLI and `git`.

.PARAMETER OutputDir
    Local directory where generated Wiki Markdown pages are written (default: ./artifacts/wiki).

.PARAMETER Push
    When specified, clones/updates the GitHub Wiki repository (<owner>/<repo>.wiki.git) resolved
    via `gh repo view` and commits + pushes any updated Wiki pages.

.EXAMPLE
    ./scripts/update-wiki.ps1 -OutputDir ./artifacts/wiki
    ./scripts/update-wiki.ps1 -Push
#>
[CmdletBinding()]
param(
    [string]$OutputDir = (Join-Path $PSScriptRoot "..\artifacts\wiki"),
    [switch]$Push
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$cliProject = Join-Path $repoRoot "src\GfxStudio.Cli\GfxStudio.Cli.csproj"
$resolvedOutputDir = [System.IO.Path]::GetFullPath($OutputDir)

Write-Host "==> Generating GfxStudio Wiki pages into '$resolvedOutputDir'..." -ForegroundColor Cyan
dotnet run --project $cliProject -- wiki --output $resolvedOutputDir
if ($LASTEXITCODE -ne 0) {
    throw "Wiki page generation failed with exit code $LASTEXITCODE."
}

if (-not $Push) {
    Write-Host "==> Done! Pass -Push to publish the generated pages to the GitHub Wiki." -ForegroundColor Green
    exit 0
}

Write-Host "==> Resolving GitHub repository via 'gh repo view'..." -ForegroundColor Cyan
$repoNameWithOwner = (gh repo view --json nameWithOwner --jq ".nameWithOwner").Trim()
if ([string]::IsNullOrWhiteSpace($repoNameWithOwner)) {
    throw "Could not resolve GitHub repository via 'gh repo view'. Ensure you are authenticated with 'gh auth login'."
}

$wikiRemoteUrl = "https://github.com/$repoNameWithOwner.wiki.git"
$tempWikiCloneDir = Join-Path ([System.IO.Path]::GetTempPath()) ("gfx-studio-wiki-" + [Guid]::NewGuid().ToString("N"))

try {
    Write-Host "==> Cloning '$wikiRemoteUrl'..." -ForegroundColor Cyan
    git clone $wikiRemoteUrl $tempWikiCloneDir
    if ($LASTEXITCODE -ne 0) {
        throw "Could not clone '$wikiRemoteUrl'. Ensure the Wiki feature is enabled in GitHub Repository Settings and at least the initial Home page has been created."
    }

    Get-ChildItem -Path $resolvedOutputDir -Filter "*.md" | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination (Join-Path $tempWikiCloneDir $_.Name) -Force
    }

    Push-Location $tempWikiCloneDir
    try {
        git add *.md
        $status = git status --porcelain
        if ([string]::IsNullOrWhiteSpace($status)) {
            Write-Host "==> GitHub Wiki is already up to date. No changes to push." -ForegroundColor Green
        }
        else {
            git commit -m "docs(wiki): update layer and effect reference from codebase metadata"
            if ($LASTEXITCODE -ne 0) {
                throw "git commit failed."
            }

            git push origin HEAD
            if ($LASTEXITCODE -ne 0) {
                throw "git push to '$wikiRemoteUrl' failed."
            }

            Write-Host "==> Successfully published updated Wiki pages to https://github.com/$repoNameWithOwner/wiki" -ForegroundColor Green
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    if (Test-Path $tempWikiCloneDir) {
        Remove-Item -Path $tempWikiCloneDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
