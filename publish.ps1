param(
    [string]$Runtime = "win-x64",
    [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "QuotaLens.csproj"
[xml]$projectXml = Get-Content -Raw -LiteralPath $project
$version = [string]($projectXml.Project.PropertyGroup.Version | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($version)) { $version = "dev" }
$output = Join-Path $PSScriptRoot "artifacts\$Runtime-v$version"
$selfContained = if ($FrameworkDependent) { "false" } else { "true" }

dotnet publish $project `
    -c Release `
    -r $Runtime `
    --self-contained $selfContained `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $output

Write-Host "Quota Lens published / 已发布：$output"
