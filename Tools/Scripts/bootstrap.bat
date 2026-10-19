@echo off
rem bootstrap.bat - restore and build the project after cloning from git
setlocal
pushd "%~dp0..\.." || exit /b 1

echo Bootstrapping Obsidian repository...

dotnet restore Engine.slnx
if errorlevel 1 (
    echo.
    echo ERROR: dotnet restore failed.
    popd
    exit /b 1
)

echo Restoring complete.
echo Building solution...

dotnet build Engine.slnx /property:GenerateFullPaths=true /consoleloggerparameters:NoSummary;ForceNoAlign
if errorlevel 1 (
    echo.
    echo ERROR: dotnet build failed.
    popd
    exit /b 1
)

echo.
echo Bootstrap complete.
echo You can now open the repo in VS Code or run the project with:
echo   dotnet run --project Engine
popd
endlocal
exit /b 0
