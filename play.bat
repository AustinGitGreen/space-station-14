@echo off
rem One-click launcher: builds, starts the server in a minimized window,
rem connects the client to it, and stops the server when the client closes.
cd /d "%~dp0"

echo Building server and client...
dotnet build Content.Server --nologo -v q || goto :fail
dotnet build Content.Client --nologo -v q || goto :fail

echo Starting server...
start "SS14 Server" /min cmd /c dotnet run --no-build --project Content.Server -- --cvar game.map=Box --cvar npc.crew_roundstart_count=20

echo Waiting for the server to start...
timeout /t 15 /nobreak >nul

echo Starting client...
dotnet run --no-build --project Content.Client -- --connect --connect-address localhost

echo Client closed, stopping server...
taskkill /fi "WINDOWTITLE eq SS14 Server*" /t /f >nul 2>&1
exit /b 0

:fail
echo.
echo Build failed - see the errors above.
pause
exit /b 1
