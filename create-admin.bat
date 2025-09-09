@echo off
echo Creating admin user...
curl -X POST http://localhost:5000/api/auth/create-admin
echo.
echo Admin user creation completed!
pause
