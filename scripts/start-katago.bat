@echo off
REM =====================================================
REM KataGo GTP 启动脚本（Windows）
REM 用法：双击运行 或在 CMD 里 .\start-katago.bat
REM =====================================================

setlocal

REM === 配置（按你的实际路径修改） ===
set KATAGO_DIR=C:\Tools\KataGo
set WEIGHTS_DIR=%KATAGO_DIR%\weights
set CONFIG=%KATAGO_DIR%\default_gtp.cfg

REM 自动找最新的权重文件
set WEIGHT_FILE=
for /f "delims=" %%f in ('dir /b /a-d "%WEIGHTS_DIR%\kata1-b18*.txt.gz" 2^>nul') do (
    set WEIGHT_FILE=%WEIGHTS_DIR%\%%f
)

if "%WEIGHT_FILE%"=="" (
    echo.
    echo =====================================================
    echo  ERROR: 找不到权重文件
    echo =====================================================
    echo 请先到 %WEIGHTS_DIR%\
    echo 下载并解压权重（推荐 kata1-b18c384nbt 系列）
    echo 参考 docs\katago-setup.md
    echo =====================================================
    pause
    exit /b 1
)

if not exist "%KATAGO_DIR%\katago.exe" (
    echo.
    echo =====================================================
    echo  ERROR: 找不到 katago.exe
    echo =====================================================
    echo 请确认 KataGo 已正确解压到 %KATAGO_DIR%\
    echo 参考 docs\katago-setup.md
    echo =====================================================
    pause
    exit /b 1
)

echo.
echo =====================================================
echo   KataGo GTP 服务启动
echo =====================================================
echo   引擎: %KATAGO_DIR%\katago.exe
echo   权重: %WEIGHT_FILE%
echo   配置: %CONFIG%
echo.
echo   首次启动会编译神经网络（30s - 3min 正常）
echo   看到 ^"KataGo ^>^" 提示符就 OK
echo   Ctrl+C 退出
echo =====================================================
echo.

cd /d "%KATAGO_DIR%"
katago.exe gtp -model "%WEIGHT_FILE%" -config "%CONFIG%"

pause
