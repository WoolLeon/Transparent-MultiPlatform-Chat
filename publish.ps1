$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'MultiChatOverlay.App\MultiChatOverlay.App.csproj'
$output = Join-Path $PSScriptRoot 'publish'
$runtimeInstaller = Join-Path $output 'MicrosoftEdgeWebView2Setup.exe'

New-Item -ItemType Directory -Force -Path $output | Out-Null

# Build with trimming (Native AOT disabled by default - set PublishAot=true in csproj to enable)
# For Native AOT: dotnet publish $project -c Release -r win-x64 -p:PublishAot=true -o $output
dotnet publish $project -c Release -r win-x64 --self-contained true -o $output

# Download WebView2 bootstrapper for installer
$webView2Bootstrapper = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703'
Invoke-WebRequest -Uri $webView2Bootstrapper -OutFile $runtimeInstaller

Write-Host "Published application and WebView2 bootstrapper to $output"

# Show output size
$files = Get-ChildItem $output -File
$totalSize = ($files | Measure-Object -Property Length -Sum).Sum
Write-Host "Total publish size: $([math]::Round($totalSize/1MB, 1)) MB"