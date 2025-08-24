# Define paths
$src  = 'C:\Users\dan\BootstrapperScript\BootstrapperScript\bootstrapper.ps1'
$out  = 'C:\Users\dan\BootstrapperScript\BootstrapperScript\Bootstrapper.exe'
$icon = 'C:\Users\dan\BootstrapperScript\BootstrapperScript\AdvTechLogo.ico'    # adjust if you have a custom icon

# Compile to EXE (GUI-less, requires elevation)
Invoke-PS2EXE `
  -InputFile      $src `
  -OutputFile     $out `
  -NoConsole `
  -RequireAdmin `
  -IconFile       $icon