#Requires -RunAsAdministrator
param(
    [string]$LocalAddress = '192.168.1.6',
    [string]$RemoteSubnet = '192.168.1.0/24',
    [string]$ResultPath = ''
)
$ErrorActionPreference = 'Stop'
try {
    $programPath = Join-Path $PSScriptRoot 'DMI-Web.exe'
    if (-not (Test-Path -LiteralPath $programPath)) { throw 'DMI-Web.exe is missing.' }
    $adapter = Get-NetIPAddress -AddressFamily IPv4 -IPAddress $LocalAddress
    $profile = Get-NetConnectionProfile -InterfaceIndex $adapter.InterfaceIndex
    if ($profile.NetworkCategory -ne 'Private') { throw 'The selected adapter must already use the Private network profile.' }
    $ruleName = 'Codex-TSW7-DMI-Web-LAN-31271'
    $parameters = @{
        Name = $ruleName
        DisplayName = 'TSW 7 DMI Web - LAN 31271'
        Description = 'Allow the DMI display from the selected LAN subnet. TSW API 31270 remains local.'
        Direction = 'Inbound'
        Action = 'Allow'
        Enabled = 'True'
        Profile = 'Private'
        Protocol = 'TCP'
        LocalPort = 31271
        LocalAddress = $LocalAddress
        RemoteAddress = $RemoteSubnet
        InterfaceAlias = $adapter.InterfaceAlias
        Program = $programPath
        EdgeTraversalPolicy = 'Block'
    }
    if (Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue) {
        Set-NetFirewallRule @parameters | Out-Null
    } else {
        New-NetFirewallRule @parameters | Out-Null
    }
    $result = @{ success = $true; address = $LocalAddress; port = 31271; subnet = $RemoteSubnet; rule = $ruleName }
    if ($ResultPath) { $result | ConvertTo-Json | Set-Content -LiteralPath $ResultPath -Encoding UTF8 }
    Write-Output "LAN URL: http://${LocalAddress}:31271/"
} catch {
    if ($ResultPath) { @{ success = $false; error = $_.Exception.Message } | ConvertTo-Json | Set-Content -LiteralPath $ResultPath -Encoding UTF8 }
    throw
}
