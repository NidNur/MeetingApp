<#
.SYNOPSIS
Builds a local MeetingApp demo package without uploading it.
.DESCRIPTION
The API publish target builds Angular once when PublishFrontend=true.
Each run creates a new directory and ZIP; existing output is never deleted.
.PARAMETER OutputFolder
Parent output directory. Relative paths are resolved from the repository root.
#>
[CmdletBinding()]
param(
    [string]$OutputFolder
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$apiProject = Join-Path $repoRoot 'MeetingApp.Api\MeetingApp.Api.csproj'

if (-not (Test-Path -LiteralPath $apiProject -PathType Leaf)) {
    throw 'MeetingApp.Api.csproj was not found relative to this script.'
}

$null = Get-Command dotnet -ErrorAction Stop

if ([string]::IsNullOrWhiteSpace($OutputFolder)) {
    $OutputFolder = Join-Path $repoRoot 'artifacts\demo'
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputFolder)) {
    $OutputFolder = Join-Path $repoRoot $OutputFolder
}

$outputRoot = [System.IO.Path]::GetFullPath($OutputFolder)
if ([System.IO.File]::Exists($outputRoot)) {
    throw 'OutputFolder must be a directory, not an existing file.'
}

$runId = '{0}-{1}' -f (Get-Date -Format 'yyyyMMdd-HHmmss'), [Guid]::NewGuid().ToString('N')
$runDirectory = Join-Path $outputRoot $runId
$siteDirectory = Join-Path $runDirectory 'site'
$archivePath = Join-Path $runDirectory 'MeetingApp-demo.zip'

if (Test-Path -LiteralPath $runDirectory) {
    throw 'The generated output directory already exists. Run the script again.'
}

$null = [System.IO.Directory]::CreateDirectory($runDirectory)
$publishArguments = @(
    'publish', $apiProject,
    '--configuration', 'Release',
    '--output', $siteDirectory,
    '--self-contained', 'false',
    '-p:PublishFrontend=true',
    '-p:EnvironmentName=Production',
    '-p:UseAppHost=false'
)

Push-Location -LiteralPath $repoRoot
try {
    & dotnet @publishArguments
    $publishExitCode = $LASTEXITCODE
    if ($publishExitCode -ne 0) {
        throw ('dotnet publish failed with exit code {0}. No ZIP was created.' -f $publishExitCode)
    }
}
finally {
    Pop-Location
}

foreach ($requiredFile in @('MeetingApp.Api.dll', 'web.config', 'wwwroot\index.html')) {
    if (-not (Test-Path -LiteralPath (Join-Path $siteDirectory $requiredFile) -PathType Leaf)) {
        throw ('Publish output is incomplete: {0}. No ZIP was created.' -f $requiredFile)
    }
}

$sensitiveFiles = @(Get-ChildItem -LiteralPath $siteDirectory -File -Recurse -Force |
    Where-Object {
        $_.Name -eq '.env' -or $_.Name -like '.env.*' -or $_.Name -eq 'secrets.json' -or
        $_.Extension -in @('.pfx', '.p12', '.key', '.pem', '.publishSettings', '.user')
    })
if ($sensitiveFiles.Count -gt 0) {
    throw 'Publish output contains a private configuration or credential file. No ZIP was created.'
}

$storageDirectory = Join-Path $siteDirectory 'Storage'
if ((Test-Path -LiteralPath $storageDirectory) -and
    @(Get-ChildItem -LiteralPath $storageDirectory -File -Recurse -Force).Count -gt 0) {
    throw 'Publish output contains uploaded Storage files. No ZIP was created.'
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $siteDirectory,
    $archivePath,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false
)

Write-Host 'Local demo package is ready. Nothing was uploaded.'
[pscustomobject]@{
    SiteDirectory = $siteDirectory
    ArchivePath = $archivePath
}
