param([switch]$SkipChecks)
$ErrorActionPreference='Stop'
$previewRoot=Split-Path $PSScriptRoot -Parent
[xml]$props=Get-Content -LiteralPath (Join-Path $previewRoot 'Directory.Build.props')
if([string]$props.Project.PropertyGroup.Version -notmatch '-preview'){throw 'This command is for preview versions.'}
$destination=Join-Path $previewRoot 'publish\SoundstageIRGenerator-preview'
& (Join-Path $previewRoot 'build.ps1') -SkipTests -OutputDirectory $destination
if(-not $SkipChecks){
    $report=Join-Path $previewRoot 'artifacts\language-preview'
    $check=Start-Process -FilePath (Join-Path $destination 'SoundstageIRGenerator.exe') -ArgumentList @('--language-check',('"'+$report+'"')) -WindowStyle Hidden -Wait -PassThru
    if($check.ExitCode -ne 0){throw "Bilingual checks failed. See $report"}
}
Write-Output "Preview: $destination"
