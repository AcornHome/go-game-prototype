$ErrorActionPreference = 'Stop'
Set-Location 'C:\Users\Administrator\WorkBuddy\2026-09-04-12-30-09\go-game-prototype\serverless\fc\'

if (Test-Path chinago-feedback.zip) { Remove-Item chinago-feedback.zip -Force }

# 写到临时目录：把所有要打包的文件先集中起来（去掉隐藏/系统文件）
$tmp = "$env:TEMP\fc-pack-$(Get-Random)"
$null = New-Item -ItemType Directory -Path $tmp -Force

# 复制 index.js, package.json, package-lock.json
Copy-Item -Path '.\index.js','.\package.json','.\package-lock.json' -Destination $tmp

# 复制 node_modules（用 robocopy 跳过锁定文件）
robocopy '.\node_modules' "$tmp\node_modules" /E /XF ".npm" 2>&1 | Out-Null

# Compress-Archive 打 zip
Compress-Archive -Path "$tmp\*" -DestinationPath '.\chinago-feedback.zip' -CompressionLevel Optimal

Remove-Item -Recurse -Force $tmp

Write-Host "OK"
Get-Item .\chinago-feedback.zip | Select-Object Name, Length, LastWriteTime
