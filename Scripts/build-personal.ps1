param(
    [string]$Configuration = "Release",
    [string]$Dotnet = "dotnet"
)
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    $projects = @("Flow.Launcher/Flow.Launcher.csproj")
    $projects += Get-ChildItem -LiteralPath "Plugins" -Filter "*.csproj" -Recurse | ForEach-Object FullName
    $projects += Get-ChildItem -LiteralPath "PersonalPlugins" -Filter "*.csproj" -Recurse | ForEach-Object FullName
    foreach ($project in $projects) {
        & $Dotnet build "$project" -c $Configuration --nologo
        if ($LASTEXITCODE -ne 0) { throw "构建失败：$project" }
    }
    & $Dotnet publish "Flow.Launcher/Flow.Launcher.csproj" -c $Configuration -r win-x64 --self-contained true -o ".artifacts/publish" --nologo -p:GeneratePackageOnBuild=false
    if ($LASTEXITCODE -ne 0) { throw "便携版发布构建失败。" }
    Write-Host "个人主程序与两个独立插件构建完成。"
} finally {
    Pop-Location
}
