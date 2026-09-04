# -------------------------------------------------------------------------------
# Sad2Keyをランタイム同梱の単一exeとして dist フォルダへ公開するスクリプト
# 使い方: PowerShellで  .\publish.ps1
# -------------------------------------------------------------------------------
$ErrorActionPreference = "Stop"
$projectPath = Join-Path $PSScriptRoot "Sad2Key\Sad2Key.csproj"
$distPath = Join-Path $PSScriptRoot "dist"

dotnet publish $projectPath -p:PublishProfile=SingleFile
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

Write-Host ""
Write-Host "Published to: $distPath"
Get-ChildItem $distPath | Select-Object Name, @{ Name = "SizeMB"; Expression = { [Math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize
