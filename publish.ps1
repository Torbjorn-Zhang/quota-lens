param(
    [string]$Runtime = "win-x64",
    [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "QuotaLens.csproj"
[xml]$propsXml = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot "Directory.Build.props")
$version = [string]($propsXml.Project.PropertyGroup.Version | Select-Object -First 1)
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
