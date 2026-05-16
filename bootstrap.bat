@echo off
rem bootstrap.bat - restore and build the project after cloning from git
setlocal

necho Bootstrapping Obsidian repository...

ndotnet restore
if errorlevel 1 (
    echo.
    echo ERROR: dotnet restore failed.
    exit /b 1
)

necho Restoring complete.
echo Building solution...

ndotnet build DeferredEngine.sln /property:GenerateFullPaths=true /consoleloggerparameters:NoSummary;ForceNoAlign
if errorlevel 1 (
    echo.
    echo ERROR: dotnet build failed.
    exit /b 1
)

necho.
echo Bootstrap complete.
echo You can now open the repo in VS Code or run the project with:
echo   dotnet run --project DeferredEngine
endlocal
exit /b 0
