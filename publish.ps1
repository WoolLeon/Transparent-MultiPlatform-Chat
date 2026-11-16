$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'MultiChatOverlay.App\MultiChatOverlay.App.csproj'
$output = Join-Path $PSScriptRoot 'publish'
$runtimeInstaller = Join-Path $output 'MicrosoftEdgeWebView2Setup.exe'

New-Item -ItemType Directory -Force -Path $output | Out-Null
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $output

$webView2Bootstrapper = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703'
Invoke-WebRequest -Uri $webView2Bootstrapper -OutFile $runtimeInstaller
Write-Host "Published application and WebView2 bootstrapper to $output"
