@echo off
echo ========================================
echo BloodSuckers Slot Reel Set Generator
echo Threaded Version for 1M Reel Sets
echo ========================================
echo.

echo Starting threaded reel set generation...
echo This will generate 1,000,000 reel sets using all CPU cores
echo Each reel set will be simulated with 500,000 Monte Carlo spins
echo Estimated time: 1-2.5 days depending on your system
echo.

cd ReelSetGenerator
dotnet run --configuration Release

echo.
echo Generation complete!
pause 