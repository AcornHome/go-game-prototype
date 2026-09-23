@echo off
title Allow ChinaGo Feedback Server through firewall (OPTIONAL)

rem ---------------------------------------------------------------
rem  OPTIONAL - most users do NOT need this file.
rem
rem  If you use the wangyunchuan tunnel (rzt7s7dz.dongtaiyuming.net),
rem  the tunnel client dials OUT to the tunnel server, so Windows
rem  firewall does not block it. You can skip this file entirely.
rem
rem  You only need this if you want other PCs on your LAN (or the
rem  public IPv6 address) to reach the server directly.
rem
rem  Run as Administrator: right click -> Run as administrator
rem ---------------------------------------------------------------

netsh advfirewall firewall delete rule name="ChinaGo Feedback Server" >nul 2>&1
netsh advfirewall firewall add rule name="ChinaGo Feedback Server" dir=in action=allow protocol=TCP localport=8080

if errorlevel 1 (
  echo.
  echo  [FAILED] Please right click this file and choose "Run as administrator".
  echo.
) else (
  echo.
  echo  [OK] Port 8080 is now open for ChinaGo Feedback Server.
  echo.
  echo  Note: tunnel users do not need this. Only LAN / direct access does.
)

pause
