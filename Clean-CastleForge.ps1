<#
:::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::
:: Clean CastleForge Build Intermediates                     ::
:: GitHub: https://github.com/RussDev7/CastleForge           ::
:: Developed and maintained by RussDev7 / Discord: dannyruss ::
:::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::::

.SYNOPSIS
    Cleans CastleForge build intermediates while keeping final build/package output.

.DESCRIPTION
    Keeps the compiled/package output created by Build-CastleForge.ps1:
      - Build\Release\
      - release\*.zip

    Removes temporary/intermediate build output such as:
      - pkg\ staging folders
      - project obj\ folders
      - non-Release folders under Build\

    Optional cleanup switches can remove project bin\ folders and Visual Studio cache folders.
    Artifact-only cleanup can also remove source/input folders, leaving only final build/package output.

.USAGE
    .\Clean-CastleForge.ps1
    .\Clean-CastleForge.ps1 -WhatIf
    .\Clean-CastleForge.ps1 -RemoveBin
    .\Clean-CastleForge.ps1 -RemoveBin -RemoveVSCache
    .\Clean-CastleForge.ps1 -NoPause
    .\Clean-CastleForge.ps1 -ArtifactsOnly -ConfirmDeleteSource
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [switch]$RemoveBin,
    [switch]$RemoveVSCache,
    [switch]$ArtifactsOnly,
    [switch]$ConfirmDeleteSource,
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"

#region Paths
$RootDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$SolutionPath = Join-Path $RootDir "CastleForge.sln"
$BuildRoot = Join-Path $RootDir "Build"
$FinalBuildOutput = Join-Path $BuildRoot "Release"
$ReleaseDir = Join-Path $RootDir "release"
$PackageWorkDir = Join-Path $RootDir "pkg"
#endregion Paths

#region Console Helpers
function Write-Section {
    param([Parameter(Mandatory)][string]$Text)

    Write-Host ""
    Write-Host "============================================================"
    Write-Host $Text
    Write-Host "============================================================"
}

function Pause-BeforeExit {
    <#
    .SYNOPSIS
        Pauses before the PowerShell window closes during local use.
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

#region Validation Helpers
function Assert-CastleForgeRoot {
    <#
    .SYNOPSIS
        Prevents accidental cleanup from the wrong directory.
    #>

    if (-not (Test-Path $SolutionPath)) {
        throw "CastleForge.sln was not found. Place this script in the CastleForge repository root before running it."
    }
}

function Test-IsProtectedPath {
    <#
    .SYNOPSIS
        Protects final build/package output from accidental deletion.
    #>

    param([Parameter(Mandatory)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
    $protectedPaths = @(
        [System.IO.Path]::GetFullPath($RootDir).TrimEnd('\'),
        [System.IO.Path]::GetFullPath($FinalBuildOutput).TrimEnd('\'),
        [System.IO.Path]::GetFullPath($ReleaseDir).TrimEnd('\')
    )

    foreach ($protectedPath in $protectedPaths) {
        if ($fullPath.Equals($protectedPath, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}
#endregion Validation Helpers

#region File System Helpers
function Remove-DirectorySafe {
    <#
    .SYNOPSIS
        Removes a directory after checking against protected final output paths.
    #>

    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Reason
    )

    if (-not (Test-Path $Path)) {
        return
    }

    if (Test-IsProtectedPath $Path) {
        Write-Host "Skipped protected path: $Path"
        return
    }

    if ($PSCmdlet.ShouldProcess($Path, "Remove directory ($Reason)")) {
        Remove-Item $Path -Recurse -Force
        Write-Host "Removed: $Path"
    }
}

function Remove-PathSafe {
    <#
    .SYNOPSIS
        Removes a file or directory after checking against protected final output paths.

    .DESCRIPTION
        Used by artifact-only cleanup where root-level files and folders may be removed.
        This helper refuses to delete the repository root, Build\Release, or release output.
    #>

    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Reason
    )

    if (-not (Test-Path $Path)) {
        return
    }

    if (Test-IsProtectedPath $Path) {
        Write-Host "Skipped protected path: $Path"
        return
    }

    if ($PSCmdlet.ShouldProcess($Path, "Remove item ($Reason)")) {
        Remove-Item $Path -Recurse -Force
        Write-Host "Removed: $Path"
    }
}

function Remove-NamedIntermediateFolders {
    <#
    .SYNOPSIS
        Removes project-level obj/bin folders while avoiding final output folders.
    #>

    param(
        [Parameter(Mandatory)][string]$FolderName,
        [Parameter(Mandatory)][string]$Reason
    )

    Get-ChildItem -Path $RootDir -Directory -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -ieq $FolderName -and
            -not $_.FullName.StartsWith($FinalBuildOutput, [System.StringComparison]::OrdinalIgnoreCase) -and
            -not $_.FullName.StartsWith($ReleaseDir, [System.StringComparison]::OrdinalIgnoreCase) -and
            -not $_.FullName.StartsWith((Join-Path $RootDir ".git"), [System.StringComparison]::OrdinalIgnoreCase)
        } |
        Sort-Object FullName -Descending |
        ForEach-Object {
            Remove-DirectorySafe -Path $_.FullName -Reason $Reason
        }
}
#endregion File System Helpers

#region Artifact-Only Helpers
function Invoke-ArtifactOnlyCleanup {
    <#
    .SYNOPSIS
        Removes source/input folders and keeps only final build/package artifacts.

    .DESCRIPTION
        This is intentionally gated behind -ConfirmDeleteSource because it removes the
        CastleForge source tree, ReferenceAssemblies, repository metadata, and other
        root-level files that are not final build/package output.
    #>

    Write-Section "Artifact-only cleanup"

    if (-not $ConfirmDeleteSource) {
        throw "Artifact-only cleanup deletes source/input folders such as CastleForge and ReferenceAssemblies. Re-run with -ArtifactsOnly -ConfirmDeleteSource to continue."
    }

    $itemsToKeep = @(
        "Build",
        "release",
        "Build-CastleForge.ps1",
        "Clean-CastleForge.ps1"
    )

    Get-ChildItem -Path $RootDir -Force | ForEach-Object {
        if ($itemsToKeep -contains $_.Name) {
            Write-Host "Keeping: $($_.FullName)"
            return
        }

        Remove-PathSafe -Path $_.FullName -Reason "artifact-only cleanup"
    }

    if (Test-Path $BuildRoot) {
        Get-ChildItem -Path $BuildRoot -Force | ForEach-Object {
            if ($_.Name -ieq "Release") {
                Write-Host "Keeping: $($_.FullName)"
                return
            }

            Remove-PathSafe -Path $_.FullName -Reason "non-final Build output"
        }
    }

    Write-Host ""
    Write-Host "Artifact-only cleanup kept:"
    Write-Host "  $FinalBuildOutput"
    Write-Host "  $ReleaseDir"
    Write-Host "  Build-CastleForge.ps1"
    Write-Host "  Clean-CastleForge.ps1"
}
#endregion Artifact-Only Helpers

#region Main Clean Flow
function Invoke-CastleForgeClean {
    Assert-CastleForgeRoot

    Write-Section "Cleaning CastleForge build intermediates"
    Write-Host "Root:                 $RootDir"
    Write-Host "Keeping build output: $FinalBuildOutput"
    Write-Host "Keeping release ZIPs: $ReleaseDir"
    Write-Host "Remove bin folders:   $RemoveBin"
    Write-Host "Remove VS cache:      $RemoveVSCache"
    Write-Host "Artifact-only mode:   $ArtifactsOnly"

    Write-Section "Removing package staging folder"
    Remove-DirectorySafe -Path $PackageWorkDir -Reason "Build-CastleForge package staging folder"

    Write-Section "Removing non-release Build folders"
    if (Test-Path $BuildRoot) {
        Get-ChildItem -Path $BuildRoot -Directory -Force |
            Where-Object { $_.Name -ine "Release" } |
            Sort-Object FullName -Descending |
            ForEach-Object {
                Remove-DirectorySafe -Path $_.FullName -Reason "non-final Build output"
            }
    }
    else {
        Write-Host "Build folder was not found. Nothing to clean there."
    }

    Write-Section "Removing obj folders"
    Remove-NamedIntermediateFolders -FolderName "obj" -Reason "MSBuild intermediate output"

    if ($RemoveBin) {
        Write-Section "Removing bin folders"
        Remove-NamedIntermediateFolders -FolderName "bin" -Reason "project-local binary output"
    }
    else {
        Write-Section "Skipping bin folders"
        Write-Host "Use -RemoveBin to remove project-local bin folders as well."
    }

    if ($RemoveVSCache) {
        Write-Section "Removing Visual Studio cache"
        Remove-DirectorySafe -Path (Join-Path $RootDir ".vs") -Reason "Visual Studio cache"
    }
    else {
        Write-Section "Skipping Visual Studio cache"
        Write-Host "Use -RemoveVSCache to remove the .vs folder."
    }

    if ($ArtifactsOnly) {
        Invoke-ArtifactOnlyCleanup
    }

    Write-Section "Clean complete"
    Write-Host "Kept final build output: $FinalBuildOutput"
    Write-Host "Kept release packages:   $ReleaseDir"
}
#endregion Main Clean Flow

#region Entry Point
try {
    Invoke-CastleForgeClean
    Pause-BeforeExit
    exit 0
}
catch {
    Write-Host ""
    Write-Host "Clean failed." -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red

    Pause-BeforeExit
    exit 1
}
#endregion Entry Point
