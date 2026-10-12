#Requires -Version 5.1
<#
.SYNOPSIS
Removes generated files without deleting application data or running packages.
.EXAMPLE
./tools/Clean.ps1 -WhatIf
.EXAMPLE
./tools/Clean.ps1 -IncludeIdeState -IncludeBuildOutputs
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [switch]$IncludeIdeState,
    [switch]$IncludeBuildOutputs
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ArtifactPolicy.ps1')
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$rootPrefix = $repositoryRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot 'KeeStashDock.sln') -PathType Leaf)) {
    throw 'Run this script from its tools directory in a KeeStashDock checkout.'
}

function Assert-SafeDirectory([string]$Path) {
    $absolutePath = [IO.Path]::GetFullPath($Path)
    if (-not $absolutePath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing a path outside the repository: $absolutePath"
    }
    $current = Get-Item -LiteralPath $absolutePath -Force
    while ($null -ne $current) {
        if (($current.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing a symbolic link or junction: $($current.FullName)"
        }
        if ($current.FullName.TrimEnd('\', '/') -eq $repositoryRoot.TrimEnd('\', '/')) { break }
        $current = $current.Parent
    }
}

function Get-ProtectedContent([string]$Path) {
    $rootReason = Get-PersonalArtifactReason (Get-Item -LiteralPath $Path -Force)
    if ($rootReason) { return $rootReason }
    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $pending.Push($Path)
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        foreach ($item in Get-ChildItem -LiteralPath $directory -Force) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                return "contains a symbolic link or junction: $($item.FullName)"
            }
            $reason = Get-PersonalArtifactReason $item
            if ($reason) { return "contains $reason" }
            if ($item.PSIsContainer) { $pending.Push($item.FullName) }
        }
    }
    return $null
}

$runningPaths = @(Get-Process | ForEach-Object {
    try { if ($_.Path) { $_.Path } } catch { }
})
$candidates = New-Object 'System.Collections.Generic.List[string]'
foreach ($sourceFolder in 'src', 'tests') {
    foreach ($project in Get-ChildItem -LiteralPath (Join-Path $repositoryRoot $sourceFolder) -Directory -Force) {
        foreach ($outputName in 'bin', 'obj') { $candidates.Add((Join-Path $project.FullName $outputName)) }
    }
}
$candidates.Add((Join-Path $repositoryRoot 'TestResults'))
if ($IncludeIdeState) { $candidates.Add((Join-Path $repositoryRoot '.vs')) }
if ($IncludeBuildOutputs) {
    foreach ($outputName in 'artifacts', 'publish') {
        $outputPath = Join-Path $repositoryRoot $outputName
        if (Test-Path -LiteralPath $outputPath -PathType Container) {
            Assert-SafeDirectory $outputPath
            foreach ($output in Get-ChildItem -LiteralPath $outputPath -Directory -Force) {
                $candidates.Add($output.FullName)
            }
        }
    }
}

$removed = 0
foreach ($candidate in $candidates) {
    if (-not (Test-Path -LiteralPath $candidate -PathType Container)) { continue }
    try {
        Assert-SafeDirectory $candidate
        $candidatePrefix = [IO.Path]::GetFullPath($candidate).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
        if ($runningPaths | Where-Object { $_.StartsWith($candidatePrefix, [StringComparison]::OrdinalIgnoreCase) }) {
            Write-Warning "Preserved running application: $candidate"
            continue
        }
        $protected = Get-ProtectedContent $candidate
        if ($protected) {
            Write-Warning "Preserved $candidate ($protected)."
            continue
        }
        if ($PSCmdlet.ShouldProcess($candidate, 'Remove generated directory')) {
            Assert-SafeDirectory $candidate
            Remove-Item -LiteralPath $candidate -Recurse -Force
            $removed++
        }
    } catch {
        Write-Warning "Could not clean $candidate. $($_.Exception.Message)"
    }
}
Write-Output "Removed $removed generated directories."
