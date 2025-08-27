<#
.SYNOPSIS
  Bootstrapper EXE that deploys the onboarding UI and desktop shortcut.
  Also moves Assets now required on disk as well.

.DESCRIPTION
  1. Finds its own folder at runtime (no $PSScriptRoot).
  2. Copies WS_Setup_6.UI.exe and AdvTechLogo.ico into C:\Working.
     2.1 Copies Assets now as well after removing Assets from exe at build time.
  3. Copies SignCode_Expires_20260709.pfx into C:\Working and hides it.
  4. Installs the PFX certificate into LocalMachine\Root and LocalMachine\TrustedPublisher.
     - If the certificate already exists, it skips re-importing.
  5. Creates “Onboard” shortcut on the current user’s desktop linked to WS.Setup.UI.exe.
  6. Logs all actions to C:\Working\bootstrap.log.
#>

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
  param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string] $Message
  )
  $ts = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
  "$ts  $Message" | Out-File -FilePath $LogFile -Append -Encoding utf8
}

# Ensure the target directory exists
if (-not (Test-Path $TargetDir)) {
  New-Item -Path $TargetDir -ItemType Directory -Force | Out-Null
  Write-Log -Message "Created target directory: $TargetDir"
}

# -------------------------------------------------------------------
# WPF UI Overlay
# -------------------------------------------------------------------

Add-Type -AssemblyName PresentationCore,PresentationFramework,WindowsBase

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

# Parse XAML & grab handles
$reader     = [System.Xml.XmlReader]::Create((New-Object System.IO.StringReader $Xaml))
$window     = [Windows.Markup.XamlReader]::Load($reader)
$stepLabel  = $window.FindName("StepLabel")
$progress   = $window.FindName("ProgressBar")
$closeBtn   = $window.FindName("CloseButton")
$doneBtn    = $window.FindName("DoneButton")
$fade       = New-Object Windows.Media.Animation.DoubleAnimation(0,1,[TimeSpan]::FromSeconds(0.5))
$dispatcher = [System.Windows.Threading.Dispatcher]::CurrentDispatcher

# Window dragging + close wiring
$window.add_MouseLeftButtonDown({
  param($s,$e)
  if ($e.ChangedButton -eq 'Left') { $window.DragMove() }
})
$closeBtn.Add_Click({ Close-UI })
$doneBtn.Add_Click({ Close-UI })

function Initialize-UI {
  $screenW = [System.Windows.SystemParameters]::PrimaryScreenWidth
  $screenH = [System.Windows.SystemParameters]::PrimaryScreenHeight
  $window.Left = ($screenW - $window.Width) / 2
  $window.Top  = ($screenH - $window.Height) / 2
  $window.Show()
  $dispatcher.Invoke([Action]{}, 'Background')
}

function Update-UI {
  param(
    [string] $Text,
    [int]    $Percent
  )
  $stepLabel.Text = $Text
  $progress.Value = $Percent
  if ($Percent -ge 100) {
    $doneBtn.Visibility = 'Visible'
    $doneBtn.BeginAnimation([Windows.UIElement]::OpacityProperty, $fade)
  }
  $dispatcher.Invoke([Action]{}, 'Background')
}

function Close-UI {
  Write-Log -Message "User closed window at $(Get-Date)"
  $window.Close()
  $dispatcher.InvokeShutdown()
}

# -------------------------------------------------------------------
# Main execution
# -------------------------------------------------------------------

# Exclude from Windows Defender
$path = $TargetDir
$pref = Get-MpPreference
if (-not ($pref.ExclusionPath -contains $path)) {
  Add-MpPreference -ExclusionPath $path
}

try {
  Write-Log -Message "=== Bootstrap started ==="
  Initialize-UI

  # 1. Determine source folder
  $exePath = [System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
  $exeDir  = Split-Path -Parent $exePath

  # 2. Copy EXE
  Update-UI -Text "Copying application…" -Percent 20
  Copy-Item (Join-Path $exeDir $AppName) (Join-Path $TargetDir $AppName) -Force
  Write-Log -Message "Copied $AppName to $TargetDir\$AppName"

  # 2.1 Copy Assets folder
  Update-UI -Text "Copying assets…" -Percent 30
  Copy-Item (Join-Path $exeDir "Assets") (Join-Path $TargetDir "Assets") -Recurse -Force
  Write-Log -Message "Copied Assets folder to $TargetDir\Assets"

  # 3. Copy & hide icon + certificate  
  Update-UI -Text "Copying and hiding assets…" -Percent 50  
  $destPfx = $null  
  
  foreach ($asset in @($IconName, $PfxName)) {  
      # build paths  
      $srcFile  = Join-Path -Path $exeDir -ChildPath "Assets\$asset"  
      $destFile = Join-Path -Path $TargetDir -ChildPath $asset  
  
      if (-not (Test-Path -Path $srcFile)) {  
          Write-Log -Message "WARNING: Asset not found: $srcFile"  
          continue  
      }  
  
      # copy  
      Copy-Item -Path $srcFile -Destination $destFile -Force  
      Write-Log -Message "Copied '$asset' to '$destFile'"  
  
      # hide  
      $file = Get-Item -Path $destFile  
      $file.Attributes = $file.Attributes -bor [System.IO.FileAttributes]::Hidden  
      Write-Log -Message "Set Hidden attribute on '$asset'"  
  
      # remember PFX for cert install  
      if ($asset -eq $PfxName) {  
          $destPfx = $destFile  
      }  
  }

  # 4. Install certificate
  Update-UI -Text "Installing certificate…" -Percent 70

  if ($destPfx) {
    $securePwd = ConvertTo-SecureString -String $PfxPass -AsPlainText -Force
    $rawCert   = [IO.File]::ReadAllBytes($destPfx)
    $certObj   = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
                    $rawCert, $securePwd, 'MachineKeySet,Exportable,PersistKeySet')
    foreach ($sn in 'Root','TrustedPublisher') {
      $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($sn,'LocalMachine')
      $store.Open('ReadWrite')
      if (-not $store.Certificates.Find('FindByThumbprint',$certObj.Thumbprint,$false).Count) {
        $store.Add($certObj)
        Write-Log -Message "Imported cert to $sn"
      }
      else {
        Write-Log -Message "Cert already present in $sn"
      }
      $store.Close()
    }
  }
  else {
    throw "Cannot install certificate—`$destPfx is null"
  }

  # 5. Create desktop shortcut
  Update-UI -Text "Creating desktop shortcut…" -Percent 90
  $desktop = [Environment]::GetFolderPath("Desktop")
  $lnkPath = Join-Path $desktop "Onboard.lnk"
  $wsh     = New-Object -ComObject WScript.Shell
  $sc      = $wsh.CreateShortcut($lnkPath)
  $sc.TargetPath   = Join-Path $TargetDir $AppName
  $sc.IconLocation = Join-Path $TargetDir $IconName
  $sc.Save()
  Write-Log -Message "Shortcut created at $lnkPath"

  # 6. Finish
  Update-UI -Text "Done with Bootstrapping!" -Percent 100
}
catch {
  Write-Log -Message "ERROR: $($_.Exception.Message)"
  throw
}
finally {
  Write-Log -Message "=== Bootstrap finished ===`n"
}

# Keep the window alive until Close-UI calls Shutdown()
[System.Windows.Threading.Dispatcher]::Run()