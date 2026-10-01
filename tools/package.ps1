param([string]$Destination)
$ErrorActionPreference='Stop'
$studioRoot=Split-Path $PSScriptRoot -Parent
if(-not $Destination){$Destination=Join-Path $studioRoot 'publish\packages'}
$Destination=[IO.Path]::GetFullPath($Destination)
[xml]$props=Get-Content -LiteralPath (Join-Path $studioRoot 'Directory.Build.props')
$version=[string]$props.Project.PropertyGroup.Version
$stamp=Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$stage=Join-Path $studioRoot ('artifacts\package-'+$stamp)
$source=Join-Path $stage 'source\SoundstageIRGenerator'
$binary=Join-Path $stage 'binary\SoundstageIRGenerator'
New-Item -ItemType Directory -Path $source,$Destination -Force | Out-Null
$rootFiles=@('.gitignore','.gitattributes','Directory.Build.props','global.json','SoundstageIRGenerator.sln','build.ps1','README.md','README.en.md','CONTRIBUTING.md','LICENSE','THIRD_PARTY_NOTICES.md')
foreach($name in $rootFiles){Copy-Item -LiteralPath (Join-Path $studioRoot $name) -Destination $source}
foreach($dir in @('App','SpeakerApp','Core','Tests','tools','docs','.github')){
    $base=Join-Path $studioRoot $dir
    foreach($file in Get-ChildItem -LiteralPath $base -File -Recurse -Force){
        $relative=$file.FullName.Substring($studioRoot.Length+1)
        if($relative -match '(^|\\)(bin|obj|__pycache__|\.venv)(\\|$)' -or $file.Name -match '\.(user|pyc|log)$'){continue}
        $to=Join-Path $source $relative
        New-Item -ItemType Directory -Path (Split-Path $to) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $to
    }
}
# Build from the allowlisted copy, proving it does not need private working files.
& (Join-Path $source 'build.ps1') -SkipTests -OutputDirectory $binary
if($LASTEXITCODE -ne 0){throw 'Clean source publish failed'}
# Build outputs are ignored; zip only the original allowlist, not generated bin/obj.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$sourceZip=Join-Path $Destination "SoundstageIRGenerator-$version-source.zip"
$binaryZip=Join-Path $Destination "SoundstageIRGenerator-$version-win-x64.zip"
if((Test-Path -LiteralPath $sourceZip) -or (Test-Path -LiteralPath $binaryZip)){throw 'Versioned package exists; choose another destination to avoid overwriting'}
$zip=[IO.Compression.ZipFile]::Open($sourceZip,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach($file in Get-ChildItem -LiteralPath $source -File -Recurse -Force){
        $rel=$file.FullName.Substring($source.Length+1)
        if($rel -match '(^|\\)(bin|obj|artifacts|publish)(\\|$)'){continue}
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$file.FullName,('SoundstageIRGenerator/'+$rel.Replace('\','/')),[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}finally{$zip.Dispose()}
$zip=[IO.Compression.ZipFile]::Open($binaryZip,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach($file in Get-ChildItem -LiteralPath $binary -File -Recurse -Force){
        if($file.Extension -eq '.pdb'){continue} # Debug symbols may embed local build paths.
        $rel=$file.FullName.Substring($binary.Length+1)
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$file.FullName,('SoundstageIRGenerator/'+$rel.Replace('\','/')),[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}finally{$zip.Dispose()}
Get-FileHash -LiteralPath $sourceZip,$binaryZip -Algorithm SHA256 | ForEach-Object { $_.Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($_.Path) } | Set-Content -LiteralPath (Join-Path $Destination 'SHA256SUMS.txt') -Encoding ascii
Write-Output "Source: $sourceZip"
Write-Output "Binary: $binaryZip"
Write-Output "Clean staging: $stage"
