param([string]$Version="1.5.0")
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dist = Join-Path $repo 'dist\NeonDeck_Portable'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
Copy-Item (Join-Path $repo 'NeonDeck.cs') $dist -Force
Copy-Item (Join-Path $repo 'Smart.ps1') $dist -Force
Copy-Item (Join-Path $repo 'NeonDeck.ico') $dist -Force
Copy-Item (Join-Path $repo 'VERSION.txt') $dist -Force
Copy-Item (Join-Path $repo 'CHANGELOG.txt') $dist -Force
Copy-Item (Join-Path $repo 'docs\README_PL.md') $dist -Force
Copy-Item (Join-Path $repo 'docs\README_EN.md') $dist -Force

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'C# compiler .NET Framework 4.x missing' }
Write-Host "Compiling WinForms dashboard..."
& $csc /nologo /target:winexe /optimize+ /codepage:65001 "/win32icon:$(Join-Path $repo 'NeonDeck.ico')" "/out:$(Join-Path $dist 'NeonDeckApp.exe')" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll (Join-Path $repo 'NeonDeck.cs')
if ($LASTEXITCODE -ne 0) { throw 'WinForms C# compilation failed' }

Write-Host "Compiling native Windows GUI launcher..."
Push-Location (Join-Path $repo 'launcher')
try {
    $env:GOOS = 'windows'; $env:GOARCH = 'amd64'; $env:CGO_ENABLED = '0'
    go build -ldflags '-H windowsgui -s -w' -o (Join-Path $dist 'NeonDeck.exe') .
    if ($LASTEXITCODE -ne 0) { throw 'Native launcher build failed' }
} finally { Pop-Location }
Set-Content -Path (Join-Path $dist 'NeonDeck.build') -Value $Version -Encoding ASCII

$zip = Join-Path $repo ('dist\NeonDeck_Windows11_Portable_v'+$Version+'.zip')
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $dist -DestinationPath $zip -Force
Write-Host "Ready: $zip"
