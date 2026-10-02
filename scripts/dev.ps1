param([ValidateSet('api','frontend','demo','test')][string]$Mode='demo')
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
Set-Location $taskRoot
$taskDotnet=Join-Path $taskRoot '.tools\dotnet\dotnet.exe'
if (Test-Path $taskDotnet) { $env:DOTNET_ROOT=Split-Path $taskDotnet; $env:DOTNET_CLI_HOME=Join-Path $taskRoot '.tools\dotnet-home'; $env:PATH=$env:DOTNET_ROOT+';'+$env:PATH } else { $taskDotnet='dotnet' }
$taskNode=Get-ChildItem (Join-Path $taskRoot '.tools\node') -Directory -ErrorAction SilentlyContinue | Select-Object -First 1
if ($taskNode) { $env:PATH=$taskNode.FullName+';'+$env:PATH }
switch ($Mode) {
  api {
    if (Test-Path '.env') { foreach ($taskLine in Get-Content '.env') { if ($taskLine -match '^([A-Za-z_][A-Za-z0-9_]*)=(.*)$') { $taskValue=$Matches[2].Trim('"',"'").Replace('\"','"'); [Environment]::SetEnvironmentVariable($Matches[1],$taskValue,'Process') } } }
    $env:ASPNETCORE_ENVIRONMENT='Development'
    & $taskDotnet run --project backend/Workout.Api --urls http://localhost:5080
  }
  frontend { Set-Location frontend; npm.cmd run dev }
  demo { Set-Location frontend; npm.cmd run demo }
  test { & $taskDotnet test backend/Workout.Tests; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; Set-Location frontend; npm.cmd test }
}
