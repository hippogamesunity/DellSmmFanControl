param([string]$Version)

$ErrorActionPreference = 'Stop'
$workspacePath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\')
$projectPath = Join-Path $workspacePath 'DellSmmFanControl\DellSmmFanControl.csproj'
if (!$Version) {
    [xml]$projectXml = Get-Content -LiteralPath $projectPath -Raw
    $Version = [string]$projectXml.Project.PropertyGroup.Version
}
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$') {
    throw 'Version must be a semantic version such as 0.1.0 or 0.1.0-beta.1.'
}

$driverHash = 'C524435027B6C252464DAB58E78CB01F7A09C820DAD870677AA15B62B974B2AF'
$driverPath = Join-Path $workspacePath 'dependencies\bzh_dell_smm_io_x64.sys'
if ((Get-FileHash -LiteralPath $driverPath -Algorithm SHA256).Hash -ne $driverHash) {
    throw 'Bundled driver checksum mismatch.'
}

$stagePath = Join-Path $workspacePath ('artifacts\staging\' + [Guid]::NewGuid().ToString('N'))
$portablePath = Join-Path $stagePath 'portable'
$releasePath = Join-Path $workspacePath 'artifacts\releases'
New-Item -ItemType Directory -Path $portablePath, $releasePath -Force | Out-Null

try {
    # This command compiles and publishes only. It never starts the application.
    & dotnet publish $projectPath --configuration Release --runtime win-x64 --self-contained true `
        -p:PublishProfile=Portable "-p:Version=$Version" --output $portablePath --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }

    # Use notices from the actual restored runtime packs, not a fixed older SDK.
    $assetsPath = Join-Path $workspacePath 'DellSmmFanControl\obj\project.assets.json'
    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
    foreach ($runtimeId in @('Microsoft.NETCore.App.Runtime.win-x64', 'Microsoft.WindowsDesktop.App.Runtime.win-x64')) {
        $download = $assets.project.frameworks.PSObject.Properties.Value.downloadDependencies |
            Where-Object { $_.name -eq $runtimeId } | Select-Object -First 1
        if (!$download) { throw "Cannot identify runtime pack: $runtimeId" }
        $runtimeVersion = $download.version.Trim('[', ']').Split(',')[0].Trim()
        $packageRelativePath = $runtimeId.ToLowerInvariant() + '/' + $runtimeVersion
        $packagePath = $null
        foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
            $absoluteFolder = if ([IO.Path]::IsPathRooted($folder)) { $folder } else { Join-Path $workspacePath $folder }
            $candidate = Join-Path $absoluteFolder $packageRelativePath
            if (Test-Path -LiteralPath $candidate) { $packagePath = $candidate; break }
        }
        if (!$packagePath) { throw "Runtime pack is missing: $runtimeId" }
        $license = @('LICENSE.TXT', 'LICENSE') | ForEach-Object { Join-Path $packagePath $_ } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if (!$license) { throw "Runtime license is missing: $runtimeId" }
        $noticeDestination = Join-Path $portablePath ('ThirdParty\' + $runtimeId)
        New-Item -ItemType Directory -Path $noticeDestination -Force | Out-Null
        Copy-Item -LiteralPath $license -Destination $noticeDestination
        $notices = Join-Path $packagePath 'THIRD-PARTY-NOTICES.TXT'
        if (Test-Path -LiteralPath $notices) { Copy-Item -LiteralPath $notices -Destination $noticeDestination }
    }

    $required = @('DellSmmFanControl.exe', 'bzh_dell_smm_io_x64.sys', 'LICENSE.md', 'NOTICE.md',
        'ThirdParty\DellSmm-LICENSE.txt', 'ThirdParty\NOTICE.txt')
    foreach ($relative in $required) {
        if (!(Test-Path -LiteralPath (Join-Path $portablePath $relative))) { throw "Missing package file: $relative" }
    }
    if ((Get-FileHash -LiteralPath (Join-Path $portablePath 'bzh_dell_smm_io_x64.sys') -Algorithm SHA256).Hash -ne $driverHash) {
        throw 'Published driver checksum mismatch.'
    }
    # .NET's ZIP API includes dotfiles (Compress-Archive can omit hidden files).
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zipName = "DellSmmFanControl-$Version-win-x64.zip"
    $temporaryZip = Join-Path $stagePath $zipName
    $archive = [IO.Compression.ZipFile]::Open($temporaryZip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $portablePath -Recurse -File -Force) {
            $entryName = $file.FullName.Substring($portablePath.Length + 1).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName,
                $entryName, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally { $archive.Dispose() }
    $destinationZip = Join-Path $releasePath $zipName
    Move-Item -LiteralPath $temporaryZip -Destination $destinationZip -Force
    $checksum = (Get-FileHash -LiteralPath $destinationZip -Algorithm SHA256).Hash.ToLowerInvariant() + "  $zipName"
    [IO.File]::WriteAllLines((Join-Path $releasePath "DellSmmFanControl-$Version-SHA256SUMS.txt"), [string[]]@($checksum))
    Write-Output $destinationZip
}
finally {
    $resolvedStage = (Resolve-Path -LiteralPath $stagePath).Path
    $expectedParent = Join-Path $workspacePath 'artifacts\staging\'
    if (!$resolvedStage.StartsWith($expectedParent, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a staging path outside $expectedParent"
    }
    Remove-Item -LiteralPath $resolvedStage -Recurse -Force
}
