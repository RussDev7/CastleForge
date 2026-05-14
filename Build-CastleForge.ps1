<#
:::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::
:: Build CastleForge Via PowerShell/MSBuild                  ::
:: GitHub: https://github.com/RussDev7/CastleForge           ::
:: Developed and maintained by RussDev7 / Discord: dannyruss ::
:::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::

.SYNOPSIS
    Builds CastleForge locally and optionally creates release ZIP packages.

.DESCRIPTION
    Mirrors the repository's GitHub release workflow locally:
      - Builds CastleForge.sln as Release|x86.
      - Verifies Visual Studio MSBuild and .NET SDK availability before building.
      - Packages the full Build\Release folder.
      - Packages ModLoader and ModLoaderExtensions.
      - Packages each official mod individually.
      - Packages each dedicated server individually.

.USAGE
    .\Build-CastleForge.ps1
    .\Build-CastleForge.ps1 -Version v0.1.0
    .\Build-CastleForge.ps1 -Version v0.1.0 -Package Full
    .\Build-CastleForge.ps1 -Version mods-v0.1.0 -Package Mods
    .\Build-CastleForge.ps1 -Package BuildOnly
    .\Build-CastleForge.ps1 -NoPause

.NOTES
    This script is intended to be run from the CastleForge repository root.
    It replaces the old Build-CastleForge.bat launcher and pauses by default
    so results remain visible when run directly.
#>

[CmdletBinding()]
param(
    [string]$Version = "",

    [ValidateSet("All", "BuildOnly", "Full", "Core", "Mods", "Servers")]
    [string]$Package = "All",

    [switch]$NoClean,
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"

#region Paths

$RootDir = if ($PSScriptRoot) {
    $PSScriptRoot
}
else {
    Split-Path -Parent $MyInvocation.MyCommand.Path
}

$SolutionPath = Join-Path $RootDir "CastleForge.sln"
$BuildOutput = Join-Path $RootDir "Build\Release"
$ModsRoot = Join-Path $BuildOutput "!Mods"
$ReleaseDir = Join-Path $RootDir "release"
$PackageWorkDir = Join-Path $RootDir "pkg"

#endregion Paths

#region Console Helpers

function Write-Section {
    <#
    .SYNOPSIS
        Writes a formatted section title to the console.
    #>

    param([Parameter(Mandatory)][string]$Text)

    Write-Host ""
    Write-Host "============================================================"
    Write-Host $Text
    Write-Host "============================================================"
}

function Pause-BeforeExit {
    <#
    .SYNOPSIS
        Pauses before closing so build results remain visible.

    .DESCRIPTION
        The pause is skipped when -NoPause is supplied or when running in
        CI/GitHub Actions.
    #>

    if ($NoPause) {
        return
    }

    if ($env:CI -eq "true" -or $env:GITHUB_ACTIONS -eq "true") {
        return
    }

    Write-Host ""
    [void](Read-Host "Press Enter to exit")
}

#endregion Console Helpers

#region File System Helpers

function New-CleanDirectory {
    <#
    .SYNOPSIS
        Creates a directory and optionally removes the previous copy first.
    #>

    param([Parameter(Mandatory)][string]$Path)

    if ((Test-Path $Path) -and (-not $NoClean)) {
        Remove-Item $Path -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $Path | Out-Null
}

function Assert-FileExists {
    <#
    .SYNOPSIS
        Throws a clear error when a required file or folder is missing.
    #>

    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Description
    )

    if (-not (Test-Path $Path)) {
        throw "$Description was not found: $Path"
    }
}

function Copy-RequiredItem {
    <#
    .SYNOPSIS
        Copies a required item and throws if the source is missing.
    #>

    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination
    )

    if (-not (Test-Path $Source)) {
        throw "Required package item was not found: $Source"
    }

    Copy-Item $Source $Destination -Recurse -Force
}

function Copy-OptionalItem {
    <#
    .SYNOPSIS
        Copies an optional item only when it exists.
    #>

    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination
    )

    if (Test-Path $Source) {
        Copy-Item $Source $Destination -Recurse -Force
    }
}

function Compress-FolderContents {
    <#
    .SYNOPSIS
        Compresses the contents of a folder into a ZIP file.
    #>

    param(
        [Parameter(Mandatory)][string]$SourceFolder,
        [Parameter(Mandatory)][string]$ZipPath
    )

    if (-not (Test-Path $SourceFolder)) {
        throw "Package source folder was not found: $SourceFolder"
    }

    $contents = Get-ChildItem -Path $SourceFolder -Force
    if (-not $contents) {
        throw "Package source folder is empty: $SourceFolder"
    }

    if (Test-Path $ZipPath) {
        Remove-Item $ZipPath -Force
    }

    Compress-Archive -Path (Join-Path $SourceFolder "*") -DestinationPath $ZipPath -Force
    Write-Host "Created: $ZipPath"
}

#endregion File System Helpers

#region Version Helpers

function Get-SafeVersionName {
    <#
    .SYNOPSIS
        Resolves a safe version string for release ZIP names.

    .DESCRIPTION
        Uses the supplied -Version value first, then git describe when available,
        then a timestamp fallback. Invalid Windows filename characters are replaced.
    #>

    param([string]$RequestedVersion)

    if (-not [string]::IsNullOrWhiteSpace($RequestedVersion)) {
        $rawVersion = $RequestedVersion.Trim()
    }
    else {
        $rawVersion = ""

        if (Get-Command git -ErrorAction SilentlyContinue) {
            try {
                $rawVersion = (& git -C $RootDir describe --tags --always --dirty 2>$null).Trim()
            }
            catch {
                $rawVersion = ""
            }
        }

        if ([string]::IsNullOrWhiteSpace($rawVersion)) {
            $rawVersion = Get-Date -Format "yyyy.MM.dd-HHmm"
        }
    }

    return ($rawVersion -replace '[\\/:*?"<>|]', '-')
}

#endregion Version Helpers

#region Build Tool Validation

function Find-MSBuild {
    <#
    .SYNOPSIS
        Locates Visual Studio 2022+ MSBuild using vswhere.

    .NOTES
        CastleForge uses projects with newer C# syntax, so VS2019/MSBuild 16.x
        is intentionally skipped.
    #>

    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"

    if (-not (Test-Path $vswhere)) {
        throw "vswhere.exe was not found. Install Visual Studio or Visual Studio Build Tools with the .NET desktop build tools workload."
    }

    $msbuild = & $vswhere `
        -latest `
        -version "[17.8,)" `
        -products * `
        -requires Microsoft.Component.MSBuild `
        -find "MSBuild\**\Bin\MSBuild.exe" |
        Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($msbuild) -or -not (Test-Path $msbuild)) {
        throw "Visual Studio 2022 Build Tools 17.8 or newer was not found. Install VS2022 Build Tools with MSBuild and the .NET desktop build tools workload."
    }

    return $msbuild
}

function Assert-DotNetSdkAvailable {
    <#
    .SYNOPSIS
        Verifies that the .NET SDK is installed and visible to this shell.

    .DESCRIPTION
        CastleForge's dedicated server projects are SDK-style projects that require
        Microsoft.NET.Sdk. Without an installed .NET SDK, MSBuild can build many
        classic .NET Framework projects but fail when it reaches the server projects.
    #>

    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue

    if (-not $dotnetCommand) {
        throw ".NET SDK was not found. Install the .NET SDK or Visual Studio Build Tools with the '.NET desktop build tools' workload."
    }

    $dotnetSdks = & dotnet --list-sdks 2>$null

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to query installed .NET SDKs using 'dotnet --list-sdks'. Reinstall or repair the .NET SDK."
    }

    if (-not $dotnetSdks) {
        throw "No .NET SDKs are installed. CastleForge's SDK-style dedicated server projects require Microsoft.NET.Sdk."
    }

    Write-Host ".NET SDKs:"
    $dotnetSdks | ForEach-Object { Write-Host "  $_" }
    Write-Host ""
}

#endregion Build Tool Validation

#region Main Build Flow

function Invoke-CastleForgeBuild {
    <#
    .SYNOPSIS
        Validates prerequisites, builds the solution, and creates requested packages.
    #>

    $safeVersion = Get-SafeVersionName $Version

    #region Validation

    Assert-FileExists $SolutionPath "CastleForge solution"
    Assert-FileExists (Join-Path $RootDir "ReferenceAssemblies\Core\CastleMinerZ.exe") "CastleMinerZ reference executable"
    Assert-FileExists (Join-Path $RootDir "ReferenceAssemblies\Core\DNA.Common.dll") "DNA.Common reference assembly"

    $msbuild = Find-MSBuild
    Assert-DotNetSdkAvailable

    #endregion Validation

    #region Build

    Write-Section "Building CastleForge Release|x86"
    Write-Host "Root:     $RootDir"
    Write-Host "Solution: $SolutionPath"
    Write-Host "MSBuild:  $msbuild"
    Write-Host "Version:  $safeVersion"
    Write-Host "Package:  $Package"

    & $msbuild $SolutionPath `
        /m `
        /restore `
        /t:Rebuild `
        /p:Configuration=Release `
        /p:Platform=x86

    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild failed with exit code $LASTEXITCODE."
    }

    Assert-FileExists (Join-Path $BuildOutput "ModLoader.dll") "ModLoader build output"
    Assert-FileExists $ModsRoot "CastleForge !Mods output folder"

    #endregion Build

    if ($Package -eq "BuildOnly") {
        Write-Section "Build complete"
        Write-Host "Output folder: $BuildOutput"
        return
    }

    #region Prepare Packaging Folders

    Write-Section "Preparing package folders"
    New-CleanDirectory $ReleaseDir
    New-CleanDirectory $PackageWorkDir

    #endregion Prepare Packaging Folders

    #region Full Release Package

    if ($Package -eq "All" -or $Package -eq "Full") {
        Write-Section "Packaging full release"

        $releaseName = "CastleForge-FullRelease-$safeVersion"
        $stageDir = Join-Path $PackageWorkDir $releaseName
        $zipPath = Join-Path $ReleaseDir "$releaseName.zip"

        New-CleanDirectory $stageDir
        Copy-RequiredItem (Join-Path $BuildOutput "*") $stageDir
        Compress-FolderContents $stageDir $zipPath
    }

    #endregion Full Release Package

    #region Core Packages

    if ($Package -eq "All" -or $Package -eq "Core") {
        Write-Section "Packaging core files"

        $modLoaderStage = Join-Path $PackageWorkDir "ModLoader"
        $modLoaderExtStage = Join-Path $PackageWorkDir "ModLoaderExtensions"

        New-CleanDirectory $modLoaderStage
        New-CleanDirectory $modLoaderExtStage

        Copy-RequiredItem (Join-Path $BuildOutput "ModLoader.dll") $modLoaderStage
        Copy-RequiredItem (Join-Path $BuildOutput "CastleMinerZ.exe.config") $modLoaderStage
        Copy-RequiredItem (Join-Path $BuildOutput "Update&Launch.bat") $modLoaderStage
        Copy-OptionalItem (Join-Path $RootDir "CastleForge\ModLoaderFramework\ModLoader\README.md") $modLoaderStage
        Copy-OptionalItem (Join-Path $RootDir "LICENSE") $modLoaderStage

        Copy-RequiredItem (Join-Path $ModsRoot "ModLoaderExtensions.dll") $modLoaderExtStage
        Copy-OptionalItem (Join-Path $ModsRoot "ModLoaderExtensions") (Join-Path $modLoaderExtStage "ModLoaderExtensions")
        Copy-OptionalItem (Join-Path $RootDir "CastleForge\ModLoaderFramework\ModLoaderExtensions\README.md") $modLoaderExtStage
        Copy-OptionalItem (Join-Path $RootDir "LICENSE") $modLoaderExtStage

        Compress-FolderContents $modLoaderStage (Join-Path $ReleaseDir "CastleForge-ModLoader-$safeVersion.zip")
        Compress-FolderContents $modLoaderExtStage (Join-Path $ReleaseDir "CastleForge-ModLoaderExtensions-$safeVersion.zip")
    }

    #endregion Core Packages

    #region Mod Packages

    if ($Package -eq "All" -or $Package -eq "Mods") {
        Write-Section "Packaging official mods individually"

        Get-ChildItem -Path $ModsRoot -Filter "*.dll" -File |
            Sort-Object Name |
            ForEach-Object {
                $modDll = $_
                $modName = [System.IO.Path]::GetFileNameWithoutExtension($modDll.Name)

                # ModLoaderExtensions is packaged with the core release.
                if ($modName -eq "ModLoaderExtensions") {
                    return
                }

                $stageDir = Join-Path $PackageWorkDir "Mods\$modName"
                $zipPath = Join-Path $ReleaseDir "$modName-$safeVersion.zip"

                New-CleanDirectory $stageDir
                Copy-RequiredItem $modDll.FullName $stageDir

                # Optional support folder from the build output, such as !Mods\SomeMod\...
                $supportFolder = Join-Path $ModsRoot $modName
                if (Test-Path $supportFolder) {
                    Copy-RequiredItem $supportFolder (Join-Path $stageDir $modName)
                }

                # Optional docs/licensing for standalone mod ZIPs.
                Copy-OptionalItem (Join-Path $RootDir "CastleForge\Mods\$modName\README.md") $stageDir
                Copy-OptionalItem (Join-Path $RootDir "LICENSE") $stageDir

                Compress-FolderContents $stageDir $zipPath
            }
    }

    #endregion Mod Packages

    #region Server Packages

    if ($Package -eq "All" -or $Package -eq "Servers") {
        Write-Section "Packaging dedicated servers individually"

        $servers = @(
            "CMZDedicatedLidgrenServer",
            "CMZDedicatedSteamServer"
        )

        foreach ($serverName in $servers) {
            $serverRoot = Join-Path $ModsRoot $serverName
            $stageDir = Join-Path $PackageWorkDir "Servers\$serverName"
            $zipPath = Join-Path $ReleaseDir "$serverName-$safeVersion.zip"

            Assert-FileExists $serverRoot "$serverName build output"

            New-CleanDirectory $stageDir
            Copy-RequiredItem (Join-Path $serverRoot "*") $stageDir
            Copy-OptionalItem (Join-Path $RootDir "CastleForge\Servers\$serverName\README.md") $stageDir
            Copy-OptionalItem (Join-Path $RootDir "LICENSE") $stageDir

            Compress-FolderContents $stageDir $zipPath
        }
    }

    #endregion Server Packages

    #region Complete

    Write-Section "Build and packaging complete"
    Write-Host "Build output: $BuildOutput"
    Write-Host "Release ZIPs: $ReleaseDir"

    #endregion Complete
}

#endregion Main Build Flow

#region Entry Point

$exitCode = 0

try {
    Invoke-CastleForgeBuild
}
catch {
    $exitCode = 1

    Write-Host ""
    Write-Host "Build failed." -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
}
finally {
    Pause-BeforeExit
}

exit $exitCode

#endregion Entry Point