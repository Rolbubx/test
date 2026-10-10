# Publishes the shippable game into GT67/Build: a Native AOT exe (no .NET install needed) with its native
# libraries, Shaders, Prefabs and Assets. Press Compile in the editor first so Generated/GeneratedGame is current.
#   ./publish-game.ps1           Native AOT (needs the Visual Studio "Desktop development with C++" tools)
#   ./publish-game.ps1 -NoAot    trimmed ReadyToRun single-file exe instead (no C++ tools needed)
param([switch]$NoAot)

# The AOT linker step runs vswhere.exe to find MSVC, but the Visual Studio installer does not put it on PATH.
$installer = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer"
if (Test-Path (Join-Path $installer "vswhere.exe")) { $env:PATH = "$installer;$env:PATH" }

$output = Join-Path $PSScriptRoot "Build"
if (Test-Path $output) { Remove-Item $output -Recurse -Force }

$arguments = @("publish", (Join-Path $PSScriptRoot "GT67.csproj"), "-c", "Game", "-r", "win-x64", "-o", $output)
if ($NoAot) { $arguments += "-p:GameAot=false" }
dotnet @arguments
if ($LASTEXITCODE -eq 0) { Write-Host "`nGame published to $output" }
exit $LASTEXITCODE
