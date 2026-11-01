param([string]$PlayniteInstallDir = 'D:\Games\Playnite')
$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$distDir = Join-Path $projectDir 'dist'
$stageDir = Join-Path $distDir 'package'
$resolvedProject = [IO.Path]::GetFullPath($projectDir).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$resolvedStage = [IO.Path]::GetFullPath($stageDir)
if (-not $resolvedStage.StartsWith($resolvedProject, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to clean a package staging path outside the project directory.'
}

$sdkDir = Get-ChildItem 'C:\Program Files\dotnet\sdk' -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (-not $sdkDir) { throw 'The .NET SDK was not found.' }
$compiler = Join-Path $sdkDir.FullName 'Roslyn\bincore\csc.dll'
$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$gac = 'C:\Windows\Microsoft.NET\assembly'
$playniteSdk = Join-Path $PlayniteInstallDir 'Playnite.SDK.dll'
if (-not (Test-Path -LiteralPath $playniteSdk)) { throw "Playnite.SDK.dll was not found at $playniteSdk" }
$referencePaths = @(
    (Join-Path $framework 'mscorlib.dll'),
    (Join-Path $framework 'System.dll'),
    (Join-Path $framework 'System.Core.dll'),
    (Join-Path $framework 'System.Security.dll'),
    (Join-Path $framework 'System.Xml.dll'),
    (Join-Path $framework 'System.Xaml.dll'),
    (Join-Path $framework 'Microsoft.CSharp.dll'),
    (Get-ChildItem "$gac\GAC_64\PresentationCore" -Recurse -Filter PresentationCore.dll | Select-Object -First 1 -ExpandProperty FullName),
    (Get-ChildItem "$gac\GAC_MSIL\PresentationFramework" -Recurse -Filter PresentationFramework.dll | Select-Object -First 1 -ExpandProperty FullName),
    (Get-ChildItem "$gac\GAC_MSIL\WindowsBase" -Recurse -Filter WindowsBase.dll | Select-Object -First 1 -ExpandProperty FullName),
    $playniteSdk
)
if ($referencePaths | Where-Object { -not (Test-Path -LiteralPath $_) }) { throw 'A required .NET or Playnite reference assembly is missing.' }
$releaseDir = Join-Path $projectDir 'bin\Release'
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
$compilerArgs = @('/noconfig','/nostdlib','/langversion:7.3','/target:library',"/out:$(Join-Path $releaseDir 'UbisoftConnectImporter.dll')")
$compilerArgs += $referencePaths | ForEach-Object { "/reference:$_" }
$compilerArgs += Get-ChildItem $projectDir -Filter '*.cs' | ForEach-Object { $_.FullName }
dotnet $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'C# compilation failed.' }

if (Test-Path -LiteralPath $stageDir) { Remove-Item -LiteralPath $stageDir -Recurse -Force }
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectDir 'extension.yaml') -Destination $stageDir
Copy-Item -LiteralPath (Join-Path $projectDir 'bin\Release\UbisoftConnectImporter.dll') -Destination $stageDir
Copy-Item -LiteralPath (Join-Path $projectDir 'README.md') -Destination $stageDir
Copy-Item -LiteralPath (Join-Path $projectDir 'LICENSE.md') -Destination $stageDir
Compress-Archive -Path (Join-Path $stageDir '*') -DestinationPath (Join-Path $distDir 'UbisoftConnectImporter.pext') -Force
Remove-Item -LiteralPath $stageDir -Recurse -Force
Write-Host "Created $(Join-Path $distDir 'UbisoftConnectImporter.pext')"
