$ErrorActionPreference = 'Stop'

# ============ Config ============
$WorkDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$KatagoDir = 'C:\Tools\KataGo'
$KatagoExe = Join-Path $KatagoDir 'katago.exe'
$WeightsDir = Join-Path $KatagoDir 'weights'
$ModelFile = Join-Path $WeightsDir 'kata1-b18c384nbt.bin.gz'
$ConfigFile = Join-Path $KatagoDir 'default_gtp.cfg'

$KatagoUrls = @(
    'https://ghfast.top/https://github.com/lightvector/KataGo/releases/download/v1.18.1/katago-v1.18.1-eigen-windows-x64.zip',
    'https://gh-proxy.com/https://github.com/lightvector/KataGo/releases/download/v1.18.1/katago-v1.18.1-eigen-windows-x64.zip',
    'https://mirror.ghproxy.com/https://github.com/lightvector/KataGo/releases/download/v1.18.1/katago-v1.18.1-eigen-windows-x64.zip'
)

# Real weight download URL (latest listed on katagotraining.org/networks/)
$WeightUrls = @(
    'https://media.katagotraining.org/uploaded/networks/models/kata1/kata1-b18c384nbt-s9996604416-d4316597426.bin.gz',
    'https://ghfast.top/https://media.katagotraining.org/uploaded/networks/models/kata1/kata1-b18c384nbt-s9996604416-d4316597426.bin.gz',
    'https://gh-proxy.com/https://media.katagotraining.org/uploaded/networks/models/kata1/kata1-b18c384nbt-s9996604416-d4316597426.bin.gz',
    'https://mirror.ghproxy.com/https://media.katagotraining.org/uploaded/networks/models/kata1/kata1-b18c384nbt-s9996604416-d4316597426.bin.gz'
)

# ============ Helpers ============
function Write-Step($n, $total, $msg) {
    Write-Host "[Step $n/$total] $msg" -ForegroundColor Cyan
}

function Try-Download($urls, $destPath) {
    foreach ($url in $urls) {
        Write-Host "  Trying: $url" -ForegroundColor Yellow
        try {
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            $ProgressPreference = 'Continue'
            Invoke-WebRequest -Uri $url -OutFile $destPath -UseBasicParsing -TimeoutSec 300
            if (Test-Path $destPath) {
                $size = (Get-Item $destPath).Length
                if ($size -gt 1MB) {
                    Write-Host "  Downloaded successfully ($([math]::Round($size/1MB, 1)) MB)" -ForegroundColor Green
                    return $true
                }
            }
        } catch {
            Write-Host "  Failed: $($_.Exception.Message)" -ForegroundColor Red
        }
    }
    return $false
}

function Manual-Katago-Instructions {
    Write-Host ""
    Write-Host "  ALL MIRRORS FAILED." -ForegroundColor Red
    Write-Host ""
    Write-Host "  Direct download URL (CPU/eigen build, no GPU needed):"
    Write-Host "    https://github.com/lightvector/KataGo/releases/download/v1.18.1/katago-v1.18.1-eigen-windows-x64.zip"
    Write-Host ""
    Write-Host "  Steps:"
    Write-Host "    1. Open browser and paste the URL above (about 6 MB)"
    Write-Host "    2. If GitHub is blocked, use a mirror:"
    Write-Host "       https://gh-proxy.com/https://github.com/lightvector/KataGo/releases/download/v1.18.1/katago-v1.18.1-eigen-windows-x64.zip"
    Write-Host "    3. Extract the zip to C:\Tools\KataGo\  (katago.exe should end up directly inside)"
    Write-Host "    4. Press Enter here to retry."
    Write-Host ""
}

function Manual-Weight-Instructions {
    Write-Host ""
    Write-Host "  ALL MIRRORS FAILED." -ForegroundColor Red
    Write-Host ""
    Write-Host "  Direct download URL (no mirror needed):"
    Write-Host "    https://media.katagotraining.org/uploaded/networks/models/kata1/kata1-b18c384nbt-s9996604416-d4316597426.bin.gz"
    Write-Host ""
    Write-Host "  Browser method (recommended):"
    Write-Host "    1. Open browser and paste the URL above (about 90 MB)"
    Write-Host "    2. If browser cannot download, try IDM / Thunder / Aria2"
    Write-Host "    3. Rename downloaded file to: kata1-b18c384nbt.bin.gz"
    Write-Host "    4. Put it in: C:\Tools\KataGo\weights\"
    Write-Host "    5. Press any key here to retry."
    Write-Host ""
    Write-Host "  PowerShell fallback (if direct link works):"
    Write-Host '    Invoke-WebRequest "https://media.katagotraining.org/uploaded/networks/models/kata1/kata1-b18c384nbt-s9996604416-d4316597426.bin.gz" -OutFile "C:\Tools\KataGo\weights\kata1-b18c384nbt.bin.gz"'
    Write-Host ""
}

# ============ Main ============
Clear-Host
Write-Host "====================================================" -ForegroundColor Cyan
Write-Host "  Go Game - First-time Setup" -ForegroundColor Cyan
Write-Host "====================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "[Debug] WorkDir:    $WorkDir"
Write-Host "[Debug] KatagoDir:  $KatagoDir"
Write-Host ""

# --- Step 1: .NET ---
Write-Step 1 4 "Checking .NET..."
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Write-Host "  ERROR: .NET SDK not found." -ForegroundColor Red
    Write-Host "  Install from: https://dotnet.microsoft.com/download/dotnet/8.0"
    Write-Host "  IMPORTANT: Check 'Add to PATH' during installation."
    Write-Host ""
    Read-Host "Press Enter to exit"
    exit 1
}
Write-Host "  Found: $($dotnet.Version)"
Write-Host ""

# --- Step 2: KataGo ---
Write-Step 2 4 "Checking KataGo..."
$needDownload = $true
if (Test-Path $KatagoExe) {
    # Try running version to verify it actually works.
    # (A CUDA build without an NVIDIA GPU fails with a missing DLL.)
    try {
        $verOut = & $KatagoExe version 2>&1 | Out-String
        if ($LASTEXITCODE -eq 0 -and $verOut -match 'KataGo') {
            Write-Host "  Already installed and working."
            $needDownload = $false
        } else {
            Write-Host "  Existing katago.exe is broken (likely CUDA build without GPU)." -ForegroundColor Yellow
            Write-Host "  Re-downloading CPU (eigen) version..."
            Remove-Item $KatagoExe -Force -ErrorAction SilentlyContinue
        }
    } catch {
        Write-Host "  Existing katago.exe failed to run. Re-downloading..." -ForegroundColor Yellow
    }
}

if ($needDownload) {
    Write-Host "  Downloading KataGo (CPU/eigen build, ~6 MB)..."
    if (-not (Test-Path $KatagoDir)) {
        New-Item -ItemType Directory -Path $KatagoDir -Force | Out-Null
    }

    $tempZip = Join-Path $env:TEMP "katago.zip"
    if (Test-Path $tempZip) { Remove-Item $tempZip -Force }

    if (Try-Download $KatagoUrls $tempZip) {
        Write-Host "  Extracting..."
        try {
            Expand-Archive -Path $tempZip -DestinationPath $KatagoDir -Force
            Remove-Item $tempZip -Force -ErrorAction SilentlyContinue

            # Move files from subdirectory if present
            $subDirs = Get-ChildItem -Path $KatagoDir -Directory
            foreach ($sub in $subDirs) {
                $subKatago = Join-Path $sub.FullName 'katago.exe'
                if (Test-Path $subKatago) {
                    Write-Host "  Moving files from $($sub.Name)..."
                    Get-ChildItem -Path $sub.FullName -Force | Move-Item -Destination $KatagoDir -Force
                    Remove-Item $sub.FullName -Recurse -Force
                    break
                }
            }
        } catch {
            Write-Host "  Extract failed: $($_.Exception.Message)" -ForegroundColor Red
        }
    } else {
        Manual-Katago-Instructions
        Read-Host "Press Enter to retry (after manual download)"
        if (-not (Test-Path $KatagoExe)) {
            Write-Host "  katago.exe still not found. Exiting."
            Read-Host "Press Enter to exit"
            exit 1
        }
    }

    if (Test-Path $KatagoExe) {
        Write-Host "  KataGo installed." -ForegroundColor Green
    } else {
        Write-Host "  ERROR: katago.exe not found after extraction." -ForegroundColor Red
        Read-Host "Press Enter to exit"
        exit 1
    }
}
Write-Host ""

# --- Step 3: Weights ---
Write-Step 3 4 "Checking neural network weights..."
if (Test-Path $ModelFile) {
    Write-Host "  Already downloaded, skipping."
} else {
    Write-Host "  Weights not found. Downloading (~150 MB)..."
    if (-not (Test-Path $WeightsDir)) {
        New-Item -ItemType Directory -Path $WeightsDir -Force | Out-Null
    }

    $tempW = Join-Path $env:TEMP "katago-model.bin.gz"
    if (Test-Path $tempW) { Remove-Item $tempW -Force }

    if (Try-Download $WeightUrls $tempW) {
        Move-Item -Path $tempW -Destination $ModelFile -Force
        Write-Host "  Weights installed." -ForegroundColor Green
    } else {
        Manual-Weight-Instructions
        Read-Host "Press Enter to retry (after manual download)"
        if (-not (Test-Path $ModelFile)) {
            Write-Host "  Weights still not found. Exiting."
            Read-Host "Press Enter to exit"
            exit 1
        }
    }
}
Write-Host ""

# --- Check default_gtp.cfg ---
if (-not (Test-Path $ConfigFile)) {
    Write-Host "  Warning: default_gtp.cfg not found." -ForegroundColor Yellow
    $exampleCfg = Join-Path $KatagoDir 'example.cfg'
    if (Test-Path $exampleCfg) {
        Copy-Item $exampleCfg $ConfigFile -Force
        Write-Host "  Copied example.cfg as default config."
    } else {
        Write-Host "  ERROR: Config file missing. Please re-extract KataGo to $KatagoDir" -ForegroundColor Red
        Read-Host "Press Enter to exit"
        exit 1
    }
}

# --- Step 4: Launch Game ---
Write-Host "====================================================" -ForegroundColor Cyan
Write-Step 4 4 "Launching game..."
Write-Host "  First-time startup compiles neural net (30s-3min)." -ForegroundColor Yellow
Write-Host "  Please wait..." -ForegroundColor Yellow
Write-Host "====================================================" -ForegroundColor Cyan
Write-Host ""

$consoleDir = Join-Path $WorkDir 'console'
if (-not (Test-Path $consoleDir)) {
    Write-Host "  ERROR: console directory missing: $consoleDir" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

Set-Location $consoleDir
& dotnet run

Write-Host ""
Write-Host "====================================================" -ForegroundColor Cyan
Write-Host "  Game ended."
Write-Host "====================================================" -ForegroundColor Cyan
Read-Host "Press Enter to exit"