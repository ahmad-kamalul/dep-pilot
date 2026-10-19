# Run as administrator. The supplied account must own the configured WSL distro.
param(
    [Parameter(Mandatory)][string]$InstallDirectory,
    [Parameter(Mandatory)][PSCredential]$ServiceCredential
)
$ErrorActionPreference = 'Stop'
$directory = (Resolve-Path -LiteralPath $InstallDirectory).Path
$executable = Join-Path $directory 'DepPilot.Agent.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw 'Publish the win-x64 Agent first.' }
if (-not (Test-Path -LiteralPath (Join-Path $directory 'appsettings.json'))) { throw 'Create appsettings.json using the supplied example first.' }
New-Service -Name 'DepPilotAgent' -DisplayName 'Dep Pilot Agent' `
    -BinaryPathName ('"{0}" --contentRoot "{1}"' -f $executable, $directory) `
    -Credential $ServiceCredential -StartupType Automatic | Out-Null
Write-Output 'Service installed. Verify WSL access and state/secret ACLs for the service account, then run Start-Service DepPilotAgent.'
