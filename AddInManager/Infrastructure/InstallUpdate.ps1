param([Parameter(Mandatory = $true)][string]$ConfigPath)

$ErrorActionPreference = 'Stop'

function Invoke-AddinUpdate {
    param($Settings, [string]$UpdateDirectory, [hashtable]$State)

    $installer = Join-Path $UpdateDirectory $Settings.InstallerName
    if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($installer)) -ne $UpdateDirectory) {
        throw $Settings.HashError
    }
    if ((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash -ne $Settings.ExpectedHash) {
        throw $Settings.HashError
    }
    Set-Content -LiteralPath (Join-Path $UpdateDirectory 'ready') -Value '' -Encoding UTF8

    # 只等待发起更新的 Revit；取消退出或非正常退出时均不安装。
    $parent = Get-Process -Id $Settings.ProcessId -ErrorAction SilentlyContinue
    if ($parent -and $parent.StartTime.ToUniversalTime().Ticks -eq $Settings.ProcessStartTicks) {
        try {
            $deadline = [DateTime]::UtcNow.AddMinutes(15)
            while (-not $parent.WaitForExit(250)) {
                if (Test-Path -LiteralPath (Join-Path $UpdateDirectory 'cancelled')) { return $false }
                if ([DateTime]::UtcNow -gt $deadline) { throw $Settings.ExitError }
            }
        }
        finally { $parent.Dispose() }
    }
    if ((Test-Path -LiteralPath (Join-Path $UpdateDirectory 'cancelled')) -or
        -not (Test-Path -LiteralPath (Join-Path $UpdateDirectory 'shutdown'))) {
        return $false
    }

    $State.Restart = $true
    if (@(Get-Process -Name Revit -ErrorAction SilentlyContinue).Count -gt 0) {
        throw $Settings.OtherRevitError
    }
    # 安装前再次校验；禁止安装器关闭其他应用或重启操作系统。
    if ((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash -ne $Settings.ExpectedHash) {
        throw $Settings.HashError
    }
    $logPath = Join-Path $UpdateDirectory 'install.log'
    $arguments = @('/SP-', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART',
        '/NOCLOSEAPPLICATIONS', '/NORESTARTAPPLICATIONS', '/RESTARTEXITCODE=3010', "/LOG=`"$logPath`"")
    $setup = Start-Process -FilePath $installer -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -Wait -PassThru
    try {
        if ($setup.ExitCode -ne 0) { throw "Installer exit code: $($setup.ExitCode)" }
    }
    finally { $setup.Dispose() }
    return $true
}

$state = @{ Restart = $false }
$completed = $false
$settings = $null
try {
    $settings = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $null = Invoke-AddinUpdate -Settings $settings -UpdateDirectory $PSScriptRoot -State $state
    $completed = $true
}
catch {
    $errorPath = Join-Path $PSScriptRoot 'update-error.log'
    $_ | Out-String | Set-Content -LiteralPath $errorPath -Encoding UTF8
    if ($settings) {
        Add-Type -AssemblyName PresentationFramework
        $null = [System.Windows.MessageBox]::Show([string]::Format($settings.ErrorFormat, $_.Exception.Message, $errorPath), $settings.ErrorTitle)
    }
}
finally {
    if ($state.Restart) {
        # 在原用户会话中重新启动同一版本 Revit，不继承安装器的提升权限。
        Start-Process -FilePath $settings.RevitPath -WindowStyle Normal
    }
    # 仅清理由插件创建的独立临时目录；失败时保留日志便于排查。
    $updateRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) 'AddInManager\Updates'))
    $directoryId = [Guid]::Empty
    if ($completed -and [IO.Path]::GetDirectoryName($PSScriptRoot) -eq $updateRoot -and
        [Guid]::TryParse([IO.Path]::GetFileName($PSScriptRoot), [ref]$directoryId)) {
        Remove-Item -LiteralPath $PSScriptRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
