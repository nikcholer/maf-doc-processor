[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$sampleRoot = $PSScriptRoot
$definitionRoot = Join-Path $sampleRoot 'source-definitions'
$outputRoot = Join-Path $sampleRoot 'sources'
$edgeCandidates = @(
    'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe',
    'C:\Program Files\Microsoft\Edge\Application\msedge.exe'
)
$edge = $edgeCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $edge) {
    throw 'Microsoft Edge is required to render the deterministic SVG fixtures on Windows.'
}

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

foreach ($definition in Get-ChildItem -LiteralPath $definitionRoot -Filter '*.svg' | Sort-Object Name) {
    [xml]$svg = Get-Content -Raw -LiteralPath $definition.FullName
    $width = [int]$svg.svg.width
    $height = [int]$svg.svg.height
    $outputPath = Join-Path $outputRoot ($definition.BaseName + '.png')
    $sourceUri = [Uri]::new($definition.FullName).AbsoluteUri
    $arguments = @(
        '--headless=new',
        '--disable-gpu',
        '--hide-scrollbars',
        '--force-device-scale-factor=1',
        "--window-size=$width,$height",
        "--screenshot=$outputPath",
        $sourceUri
    )

    $startParameters = @{
        FilePath = $edge
        ArgumentList = $arguments
        Wait = $true
        PassThru = $true
        WindowStyle = 'Hidden'
    }
    $process = Start-Process @startParameters
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $outputPath)) {
        throw "Failed to render $($definition.Name)."
    }
}

Get-ChildItem -LiteralPath $outputRoot -Filter '*.png' |
    Sort-Object Name |
    Select-Object Name, Length
