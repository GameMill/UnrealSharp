@echo off
setlocal enabledelayedexpansion

:: Define paths
set "BRANCH=main"
set "SCRIPT_DIR=%~dp0"
set "PLUGIN_ROOT=%SCRIPT_DIR%Plugins"
set "PLUGIN_DIR=%PLUGIN_ROOT%\UnrealSharp"
set "ZIP_PATH=%PLUGIN_ROOT%\%BRANCH%.zip"
set "TEMP_DIR=%PLUGIN_ROOT%\UnrealSharp-temp"
set "MAIN_BRANCH=https://github.com/UnrealSharp/UnrealSharp/archive/refs/heads/%BRANCH%.zip"

:: Ensure Plugins folder exists
if not exist "%PLUGIN_ROOT%" (
    mkdir "%PLUGIN_ROOT%"
)

:: Find .uproject file
set "UPROJECT_FILE="
for %%f in ("%SCRIPT_DIR%*.uproject") do (
    set "UPROJECT_FILE=%%f"
    goto :found_uproject
)

:found_uproject
if "!UPROJECT_FILE!"=="" (
    echo Error: No .uproject file found in %SCRIPT_DIR%
    exit /b 1
)

set "UPROJECT_PATH=!UPROJECT_FILE!"
for %%f in ("!UPROJECT_FILE!") do set "PROJECT_NAME=%%~nf"
set "PROJECT_ROOT=%SCRIPT_DIR%"
set "SOURCE_DIR=%PROJECT_ROOT%Source"

:: Parse EngineAssociation from .uproject
set "ENGINE_ASSOC="
for /f "tokens=2 delims=:," %%a in ('findstr /C:"EngineAssociation" "!UPROJECT_PATH!"') do (
    set "ENGINE_ASSOC=%%a"
    set "ENGINE_ASSOC=!ENGINE_ASSOC:"=!"
    set "ENGINE_ASSOC=!ENGINE_ASSOC: =!"
)

:: Resolve Unreal Engine path
set "UE_PATH="

:: Try registry first
for /f "tokens=2*" %%a in ('reg query "HKCU\Software\Epic Games\Unreal Engine\Builds" /v "!ENGINE_ASSOC!" 2^>nul') do (
    set "UE_PATH=%%b"
)

:: Try launcher installation
if "!UE_PATH!"=="" (
    if exist "%ALLUSERSPROFILE%\Epic\UnrealEngineLauncher\LauncherInstalled.dat" (
        for /f "usebackq tokens=1-3" %%a in ("%ALLUSERSPROFILE%\Epic\UnrealEngineLauncher\LauncherInstalled.dat") do (
            if "%%a"=="!ENGINE_ASSOC!" set "UE_PATH=%%c"
        )
    )
)

:: Try default installation path
if "!UE_PATH!"=="" (
    if exist "C:\Program Files\Epic Games\UE_!ENGINE_ASSOC!" (
        set "UE_PATH=C:\Program Files\Epic Games\UE_!ENGINE_ASSOC!"
    )
)

if "!UE_PATH!"=="" (
    echo Error: Failed to detect Unreal Engine path for version !ENGINE_ASSOC!
    exit /b 1
)

:: Scaffold Source folder if missing
if not exist "%SOURCE_DIR%" (
    echo Source folder missing — scaffolding minimal C++ module...
    
    :: Create Source directory first
    mkdir "%SOURCE_DIR%" >nul 2>&1
    
    set "MODULE_DIR=%SOURCE_DIR%\%PROJECT_NAME%"
    mkdir "!MODULE_DIR!" >nul 2>&1
    
    :: Create Build.cs file
    set "BUILD_CS_PATH=!MODULE_DIR!\!PROJECT_NAME!.Build.cs"
    (
        echo using UnrealBuildTool;
        echo.
        echo public class !PROJECT_NAME! : ModuleRules
        echo {
        echo     public !PROJECT_NAME!(ReadOnlyTargetRules Target^) : base(Target^)
        echo     {
        echo         PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        echo.
        echo         PublicDependencyModuleNames.AddRange(new string[] {
        echo             "Core",
        echo             "CoreUObject",
        echo             "Engine",
        echo             "InputCore"
        echo         }^);
        echo.
        echo         PrivateDependencyModuleNames.AddRange(new string[] { }^);
        echo     }
        echo }
    ) > "!BUILD_CS_PATH!"
    
    :: Create .cpp file
    set "CPP_PATH=!MODULE_DIR!\!PROJECT_NAME!.cpp"
    (
        echo #include "!PROJECT_NAME!.h"
        echo #include "Modules/ModuleManager.h"
        echo.
        echo IMPLEMENT_PRIMARY_GAME_MODULE( FDefaultGameModuleImpl, !PROJECT_NAME!, "!PROJECT_NAME!" ^);
    ) > "!CPP_PATH!"
    
    :: Create .h file
    set "H_PATH=!MODULE_DIR!\!PROJECT_NAME!.h"
    (
        echo #pragma once
        echo.
        echo #include "CoreMinimal.h"
    ) > "!H_PATH!"
)

:: Remove existing plugin
if exist "!PLUGIN_DIR!" (
    echo Removing existing UnrealSharp plugin...
    rmdir /s /q "!PLUGIN_DIR!" >nul 2>&1
)

:: Download UnrealSharp
echo Downloading UnrealSharp...
powershell -Command "Invoke-WebRequest -Uri '!MAIN_BRANCH!' -OutFile '!ZIP_PATH!'"
if errorlevel 1 (
    echo Error: Failed to download UnrealSharp
    exit /b 1
)

:: Extract
echo Extracting UnrealSharp...
powershell -Command "Expand-Archive -Path '!ZIP_PATH!' -DestinationPath '!TEMP_DIR!' -Force"
if not exist "!TEMP_DIR!\UnrealSharp-!BRANCH!" (
    echo Error: No extracted folder found in archive
    exit /b 1
)

move "!TEMP_DIR!\UnrealSharp-%BRANCH%" "!PLUGIN_DIR!" >nul 2>&1
rmdir /s /q "!TEMP_DIR!" >nul 2>&1
del "!ZIP_PATH!" >nul 2>&1

:: Generate project files
echo Generating Visual Studio project files...
set "UBT=!UE_PATH!\Engine\Binaries\DotNET\UnrealBuildTool\UnrealBuildTool.exe"
if exist "!UBT!" (
    "!UBT!" -projectfiles -project="!UPROJECT_PATH!" -game -engine
) else (
    echo Warning: UnrealBuildTool not found at !UBT!
    echo You may need to generate project files manually from the UE Editor
)

echo.
echo Done. You can now open your project:
echo !UPROJECT_PATH!

endlocal
