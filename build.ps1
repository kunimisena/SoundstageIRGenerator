param(
    [switch]$FullTests,
    [switch]$SkipTests,
    [switch]$HeadlessChecks,
    [string]$OutputDirectory,
    [string]$FFmpegDirectory
)
$ErrorActionPreference='Stop'
$studioRoot=$PSScriptRoot
if(-not $OutputDirectory){
    [xml]$buildVersion=Get-Content -LiteralPath (Join-Path $studioRoot 'Directory.Build.props')
    $folder=if([string]$buildVersion.Project.PropertyGroup.Version -match '-preview'){'publish\SoundstageIRGenerator-preview'}else{'publish\SoundstageIRGenerator'}
    $OutputDirectory=Join-Path $studioRoot $folder
}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
Push-Location -LiteralPath $studioRoot
try {
    dotnet build '.\SoundstageIRGenerator.sln' -c Release
    if($LASTEXITCODE -ne 0){throw 'Build failed'}
    if(-not $SkipTests){
        if($FullTests){
            dotnet run --no-build --project '.\Tests\SoundstageIR.Tests.csproj' -c Release -- "$studioRoot\artifacts\acceptance" --statistics --long
            if($LASTEXITCODE -ne 0){throw 'Full validation failed'}
        }else{
            foreach($suite in @('speakers','speaker-bands','head-bandwidth','head-reference','wet-balance','spectral','final-presets','eq-strength-only','head-only','preset-only','shared-edit','release-only','energy-only','eq-accuracy')){
                dotnet run --no-build --project '.\Tests\SoundstageIR.Tests.csproj' -c Release -- "$studioRoot\artifacts\$suite" "--$suite"
                if($LASTEXITCODE -ne 0){throw "Validation failed: $suite"}
            }
        }
    }
    dotnet publish '.\App\SoundstageIRGenerator.csproj' -c Release -r win-x64 --self-contained true -o $OutputDirectory
    if($LASTEXITCODE -ne 0){throw 'Publish failed'}
    dotnet publish '.\SpeakerApp\SoundstageSpeakers.csproj' -c Release -r win-x64 --self-contained true -o $OutputDirectory
    if($LASTEXITCODE -ne 0){throw 'Speaker publish failed'}
    # Retain notices from the exact runtime packs used by the self-contained build.
    $deps=Get-Content -LiteralPath (Join-Path $OutputDirectory 'SoundstageIRGenerator.deps.json') -Raw | ConvertFrom-Json
    $cache=$env:NUGET_PACKAGES
    if(-not $cache){$cache=Join-Path $env:USERPROFILE '.nuget\packages'}
    foreach($lib in $deps.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'runtimepack.*' }){
        $pair=$lib.Substring('runtimepack.'.Length).Split('/')
        $pack=Join-Path $cache ($pair[0].ToLowerInvariant()+'\'+$pair[1])
        $notices=Join-Path $OutputDirectory ('licenses\'+$pair[0]+'-'+$pair[1])
        New-Item -ItemType Directory -Path $notices -Force | Out-Null
        $packNotices=@(Get-ChildItem -LiteralPath $pack -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)' })
        if($packNotices.Count -eq 0){throw "Missing runtime notices: $pack"}
        foreach($notice in $packNotices){Copy-Item -LiteralPath $notice.FullName -Destination $notices -Force}
    }
    # Optional local convenience. Public packages deliberately do not bundle FFmpeg.
    if($FFmpegDirectory){
        $toolDir=Join-Path $OutputDirectory 'tools\ffmpeg'
        New-Item -ItemType Directory -Path $toolDir -Force | Out-Null
        foreach($name in @('ffmpeg.exe','ffprobe.exe')){
            $src=Join-Path $FFmpegDirectory $name
            if(-not(Test-Path -LiteralPath $src)){$src=Join-Path $FFmpegDirectory "bin\$name"}
            Copy-Item -LiteralPath $src -Destination $toolDir -Force
        }
        foreach($notice in Get-ChildItem -LiteralPath $FFmpegDirectory -File | Where-Object {$_.Name -match 'LICENSE|COPYING|NOTICE|README'}){Copy-Item -LiteralPath $notice.FullName -Destination $toolDir -Force}
    }
    Set-Content -LiteralPath (Join-Path $OutputDirectory 'portable.txt') -Value 'Project configurations and output folders live beside this executable.' -Encoding utf8
    Copy-Item -LiteralPath (Join-Path $studioRoot 'docs') -Destination $OutputDirectory -Recurse -Force
    foreach($name in @('README.md','README.en.md','CONTRIBUTING.md','THIRD_PARTY_NOTICES.md','LICENSE')){Copy-Item -LiteralPath (Join-Path $studioRoot $name) -Destination $OutputDirectory -Force}
    # Keep user configuration and output files outside the distributable package.
    if($HeadlessChecks){
        $report=Join-Path $studioRoot 'artifacts\published-headless'
        $process=Start-Process -FilePath (Join-Path $OutputDirectory 'SoundstageIRGenerator.exe') -ArgumentList @('--headless-check',('"'+$report+'"')) -WindowStyle Hidden -Wait -PassThru
        if($process.ExitCode -ne 0){throw 'Published offscreen validation failed'}
        $speakerReport=Join-Path $report 'speaker-ui-results.txt'
        $speakerCheck=Start-Process -FilePath (Join-Path $OutputDirectory 'SoundstageSpeakers.exe') -ArgumentList @('--check',('"'+$speakerReport+'"')) -WindowStyle Hidden -Wait -PassThru
        if($speakerCheck.ExitCode -ne 0){throw 'Published speaker validation failed'}
    }
}
finally {Pop-Location}
