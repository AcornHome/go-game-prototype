# =====================================================
# Go Game Prototype - 环境检查脚本
# 用法：在 PowerShell 里 .\check-env.ps1
# =====================================================

Write-Host ""
Write-Host "=== Go Game Prototype 环境检查 ===" -ForegroundColor Cyan
Write-Host ""

# 1. KataGo
Write-Host "[1] KataGo 安装检查" -ForegroundColor Yellow
$katago = "C:\Tools\KataGo\katago.exe"
if (Test-Path $katago) {
    Write-Host "  [OK] KataGo 已安装: $katago" -ForegroundColor Green
    $size = [math]::Round((Get-Item $katago).Length / 1MB, 1)
    Write-Host "       二进制大小: $size MB" -ForegroundColor Gray
} else {
    Write-Host "  [X] KataGo 未安装或路径不对: $katago" -ForegroundColor Red
    Write-Host "      参考 docs/katago-setup.md 安装" -ForegroundColor Gray
}

# 2. 权重文件
Write-Host ""
Write-Host "[2] 权重文件检查" -ForegroundColor Yellow
$weightsDir = "C:\Tools\KataGo\weights"
if (Test-Path $weightsDir) {
    $weights = Get-ChildItem $weightsDir -Filter "kata1-b18*.txt.gz" -ErrorAction SilentlyContinue
    if ($weights) {
        Write-Host "  [OK] 找到权重:" -ForegroundColor Green
        foreach ($w in $weights) {
            $size = [math]::Round($w.Length / 1MB, 1)
            Write-Host "       $($w.Name) ($size MB)" -ForegroundColor Gray
        }
    } else {
        Write-Host "  [!] weights 目录存在但没找到权重文件" -ForegroundColor DarkYellow
    }
} else {
    Write-Host "  [X] weights 目录不存在: $weightsDir" -ForegroundColor Red
}

# 3. CUDA / GPU 检查
Write-Host ""
Write-Host "[3] NVIDIA GPU 检查（可选）" -ForegroundColor Yellow
try {
    $gpu = Get-WmiObject Win32_VideoController | Where-Object { $_.Name -like "*NVIDIA*" }
    if ($gpu) {
        Write-Host "  [OK] 找到 NVIDIA GPU:" -ForegroundColor Green
        Write-Host "       $($gpu.Name)" -ForegroundColor Gray
    } else {
        Write-Host "  [!] 未找到 NVIDIA GPU（CPU 模式可用）" -ForegroundColor DarkYellow
    }
} catch {
    Write-Host "  [!] GPU 检查跳过" -ForegroundColor DarkYellow
}

# 4. Unity Hub
Write-Host ""
Write-Host "[4] Unity Hub 检查" -ForegroundColor Yellow
$unityHub = "C:\Program Files\Unity\Hub\Editor"
if (Test-Path $unityHub) {
    $editors = Get-ChildItem $unityHub -Directory -ErrorAction SilentlyContinue
    if ($editors) {
        Write-Host "  [OK] Unity 编辑器版本:" -ForegroundColor Green
        foreach ($e in $editors) {
            if ($e.Name -like "*6000.*") {
                Write-Host "       [推荐] $($e.Name) ← Unity 6 LTS" -ForegroundColor Cyan
            } else {
                Write-Host "       - $($e.Name)" -ForegroundColor Gray
            }
        }
    } else {
        Write-Host "  [!] Editor 目录为空" -ForegroundColor DarkYellow
    }
} else {
    Write-Host "  [X] Unity Hub 未安装或路径不对" -ForegroundColor Red
    Write-Host "      下载: https://unity.com/download" -ForegroundColor Gray
}

# 5. 工作区
Write-Host ""
Write-Host "[5] 工作区检查" -ForegroundColor Yellow
$projectRoot = "C:\Users\Administrator\WorkBuddy\2026-09-04-12-30-09\go-game-prototype"
if (Test-Path $projectRoot) {
    Write-Host "  [OK] 项目目录存在: $projectRoot" -ForegroundColor Green
    Write-Host ""
    Write-Host "       子目录:" -ForegroundColor Gray
    foreach ($d in @("docs", "scripts", "unity-scripts")) {
        if (Test-Path (Join-Path $projectRoot $d)) {
            Write-Host "       [OK] $d" -ForegroundColor Gray
        } else {
            Write-Host "       [X] $d" -ForegroundColor Red
        }
    }
} else {
    Write-Host "  [X] 项目目录不存在: $projectRoot" -ForegroundColor Red
}

Write-Host ""
Write-Host "=== 检查完成 ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "下一步:" -ForegroundColor Yellow
Write-Host "  1. 运行 scripts\start-katago.bat 测试 KataGo" -ForegroundColor Gray
Write-Host "  2. 打开 Unity Hub 创建 3D URP 工程（Unity 6 LTS）" -ForegroundColor Gray
Write-Host "  3. 把 unity-scripts\*.cs 复制到 Unity 工程的 Assets\Scripts\" -ForegroundColor Gray
Write-Host ""
