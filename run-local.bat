@echo off
echo Starting BloodSuckersSlot projects locally...
echo.

echo Starting API project on http://localhost:5264...
start "BloodSuckersSlot API" cmd /k "cd BloodSuckersSlot.Api && dotnet run"

echo Waiting 5 seconds for API to start...
timeout /t 5 /nobreak > nul

echo Starting Web project on http://localhost:7178...
start "BloodSuckersSlot Web" cmd /k "cd BloodSuckersSlot.Web && dotnet run"

echo.
echo Both projects are starting...
echo API: http://localhost:5264
echo Web: http://localhost:7178
echo.
echo Press any key to exit this script (projects will continue running)
pause > nul 