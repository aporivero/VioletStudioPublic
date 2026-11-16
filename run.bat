@echo off
cd /d "%~dp0"
dotnet run --project VioletStudio.Web\VioletStudio.Web.csproj --launch-profile VioletStudio.Web
pause
