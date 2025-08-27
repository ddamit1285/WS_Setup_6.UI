using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Text.Json;
using System.Threading.Tasks;
using WS_Setup_6.Common.Interfaces;
using WS_Setup_6.Common.Logging;
using WS_Setup_6.Core.Interfaces;

namespace WS_Setup_6.Core.Services
{
    [SupportedOSPlatform("windows")]
    public class OnboardService : IOnboardService
    {
        private static readonly string _baseDir = AppDomain.CurrentDomain.BaseDirectory;
        private static string AssetsDir => Path.Combine(_baseDir, "Assets");

        private readonly ILogService _log;

        public OnboardService(ILogService log)
        {
            _log = log;
        }

        // Installs the NinjaRMM agent from the given MSI path
        public async Task InstallAgentAsync(string installerPath)
        {
            const string serviceName = "NinjaRMMAgent";

            if (!File.Exists(installerPath))
                throw new FileNotFoundException("Agent MSI not found", installerPath);

            var msiexec = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "msiexec.exe");
            var args = $"/i \"{installerPath}\" /qn /norestart";

            _log.Log($"Launching Agent installer: {args}", "INFO");

            var exitCode = await RunProcessAsync(
                msiexec, args,
                onOutput: line => _log.Log(line, "INFO"),
                onError: err => _log.Log(err, "ERROR"));

            if (exitCode != 0)
            {
                _log.Log($"Agent installer exited with code {exitCode}.", "ERROR");
                return;
            }

            _log.Log("Agent MSI installed.", "INFO");
            _log.Log("Stopping Agent service…", "INFO");

            var scExe = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "sc.exe");

            await RunProcessAsync(
                scExe,
                $"stop \"{serviceName}\"",
                onOutput: line => _log.Log(line, "INFO"),
                onError: err => _log.Log($"Could not stop service: {err}", "WARN"));

            _log.Log("Agent service stopped. It will start on next reboot.", "INFO");
        }

        // Joins the machine to the specified domain using given credentials
        public Task RunDomainJoinAsync(string domainName, NetworkCredential creds)
        {
            _log.Log($"Beginning domain join: {domainName}", "INFO");

            return Task.Run(() =>
            {
                var scope = new ManagementScope(@"\\.\root\cimv2");
                scope.Connect();

                using var mc = new ManagementClass(
                    scope, new ManagementPath("Win32_ComputerSystem"), null);

                foreach (ManagementObject mo in mc.GetInstances().Cast<ManagementObject>())
                {
                    var inParams = mo.GetMethodParameters("JoinDomainOrWorkgroup");
                    inParams["Name"] = domainName;
                    inParams["UserName"] = $"{creds.Domain}\\{creds.UserName}";
                    inParams["Password"] = creds.Password;
                    inParams["FJoinOptions"] = 3;

                    var outParams = mo.InvokeMethod("JoinDomainOrWorkgroup", inParams, null);
                    var rc = Convert.ToUInt32(outParams["ReturnValue"]);
                    if (rc != 0)
                        throw new InvalidOperationException($"WMI returned {rc}");

                    _log.Log($"WMI join succeeded (rc={rc})", "DEBUG");
                    break;
                }

                _log.Log($"Joined domain {domainName}.", "SUCCESS");
            });
        }

        // Installs Google Chrome by downloading the enterprise MSI and running it
        public async Task InstallChromeAsync()
        {
            if (IsChromeInstalled())
            {
                _log.Log("Chrome already installed.", "INFO");
                return;
            }

            // target the .NET single-file extraction folder
            string extractionDir = AppDomain.CurrentDomain.BaseDirectory;
            var tempMsi = Path.Combine(extractionDir, "ChromeStandalone.msi");
            _log.Log($"Downloading Chrome MSI to {tempMsi}…", "INFO");

            using var client = new HttpClient();
            using var response = await client.GetAsync(
                "https://dl.google.com/dl/chrome/install/googlechromestandaloneenterprise64.msi",
                HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using (var remote = await response.Content.ReadAsStreamAsync())
            await using (var fs = new FileStream(
                tempMsi,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read,    // <- allow msiexec (and AV) to read while you write
                bufferSize: 81920,
                useAsync: true))
            {
                await remote.CopyToAsync(fs).ConfigureAwait(false);
                await fs.FlushAsync().ConfigureAwait(false);
            }  // <-- fs.Dispose() here, lock is released

            // give the OS a sec to finish any post-write processing
            await Task.Delay(2000).ConfigureAwait(false);

            _log.Log("Chrome MSI download complete.", "INFO");
            _log.Log("Launching Chrome installer…", "INFO");

            var msiexec = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "msiexec.exe");
            var psi = new ProcessStartInfo(
                msiexec,
                $"/i \"{tempMsi}\" /qn /norestart")
            {
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi)!;
            await proc.WaitForExitAsync().ConfigureAwait(false);

            _log.Log($"Chrome MSI exited with code {proc.ExitCode}", "INFO");
        }

		public async Task InstallAdobeReaderAsync()
		{
			// 0. Short-circuit if already installed
			if (IsAdobeReaderInstalled())
			{
				_log.Log("Adobe Reader already installed.", "INFO");
				return;
			}

			// 1. Fetch latest Adobe Reader asset from your GitHub Releases
			_log.Log("Fetching Adobe Reader release info…", "INFO");
			using var http = new HttpClient();
			http.DefaultRequestHeaders.UserAgent.ParseAdd("AdvTechSetup");

			var apiUrl =
			  "https://api.github.com/repos/ddamit1285/advtech-deploy-assets/releases/latest";
			var json = await http.GetStringAsync(apiUrl);
			using var doc = JsonDocument.Parse(json);

			var asset = doc.RootElement
				.GetProperty("assets")
				.EnumerateArray()
				.Select(a => new
				{
					Name = a.GetProperty("name").GetString()!,
					Url = a.GetProperty("browser_download_url").GetString()!
				})
				.FirstOrDefault(x =>
					x.Name.Equals("AcroRdrInstaller.exe",
								  StringComparison.OrdinalIgnoreCase));

			if (asset == null)
			{
				_log.Log("Could not locate AcroRdrInstaller.exe in GitHub assets.", "ERROR");
				return;
			}

			// 2. Ensure AssetsDir exists and compute local path
			Directory.CreateDirectory(AssetsDir);
			var installerPath = Path.Combine(AssetsDir, asset.Name);
			_log.Log($"Installer will live at: {installerPath}", "INFO");

			// 3. Download if missing
			if (!File.Exists(installerPath))
			{
				_log.Log($"Downloading {asset.Name} from GitHub…", "INFO");
				using var response = await http.GetAsync(asset.Url);
				response.EnsureSuccessStatusCode();

				await using var fs = new FileStream(
					installerPath, FileMode.Create, FileAccess.Write, FileShare.None);
				await response.Content.CopyToAsync(fs);

				_log.Log("Download complete.", "INFO");
			}

			// 4. Launch installer silently with elevated rights
			var psi = new ProcessStartInfo
			{
				FileName = installerPath,
				Arguments = "/sAll /rs /rps /msi EULA_ACCEPT=YES",
				WorkingDirectory = AssetsDir,
				UseShellExecute = true,
				Verb = "runas",
				CreateNoWindow = true
			};

			_log.Log("Launching Adobe Reader installer…", "INFO");
			using var proc = Process.Start(psi)!;
			await proc.WaitForExitAsync().ConfigureAwait(false);

			_log.Log($"Installer exited with code {proc.ExitCode}", "INFO");
			_log.Log("Adobe Reader installation complete.", "INFO");
		}

		// Sets up dependencies: PowerShell 7, network profile, PS remoting
		public async Task SetupDependenciesAsync()
        {
            // Ensure PowerShell 7 is installed
            _log.Log("Checking PowerShell 7…", "INFO");

            if (!IsPowerShell7Installed())
            {
                _log.Log("PS7 not found. Installing…", "INFO");
                await InstallPowerShell7Async().ConfigureAwait(false);
            }
            else
            {
                _log.Log("PowerShell 7 already present.", "INFO");
            }

            // Set Network Profiles private
            _log.Log("Setting network profiles to Private…", "INFO");
            await RunPwshAsync(
                "Get-NetConnectionProfile | Set-NetConnectionProfile -NetworkCategory Private -Confirm:$false",
                onOutput: output => _log.Log(output, "INFO"),
                onError: err => _log.Log(err, "ERROR")
            ).ConfigureAwait(false);

            // Enable PS Remoting
            _log.Log("Enabling PS Remoting…", "INFO");
            await RunPwshAsync(
                "Enable-PSRemoting -Force -Confirm:$false",
                onOutput: output => _log.Log(output, "INFO"),
                onError: err => _log.Log(err, "ERROR"),
                useShellExecute: true,
                verb: "runas"
            ).ConfigureAwait(false);
        }

        // Checks if Google Chrome is installed by looking in the registry
        private static bool IsChromeInstalled()
        {
            var keys = new[]
            {
                @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Uninstall",
                @"HKEY_LOCAL_MACHINE\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var key in keys)
            {
                using var rk = Registry.LocalMachine.OpenSubKey(key.Replace("HKEY_LOCAL_MACHINE\\", ""));
                if (rk == null) continue;

                foreach (var sub in rk.GetSubKeyNames())
                {
                    using var sk = rk.OpenSubKey(sub);
                    if ((sk?.GetValue("DisplayName") as string)?.Contains("Google Chrome") == true)
                        return true;
                }
            }
            return false;
        }

        // Checks if Adobe Reader is installed by looking in the registry
        private static bool IsAdobeReaderInstalled()
        {
            // Check both 32-bit and 64-bit registry locations
            var keys = new[]
            {
                @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Uninstall",
                @"HKEY_LOCAL_MACHINE\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };
            foreach (var key in keys)
            {
                using var rk = Registry.LocalMachine.OpenSubKey(key.Replace("HKEY_LOCAL_MACHINE\\", ""));
                if (rk == null) continue;
                foreach (var sub in rk.GetSubKeyNames())
                {
                    using var sk = rk.OpenSubKey(sub);
                    if ((sk?.GetValue("DisplayName") as string)?.Contains("Adobe Acrobat Reader") == true)
                        return true;
                }
            }
            return false;
        }
        // Checks if PowerShell 7 is installed by looking for pwsh.exe
        private static bool IsPowerShell7Installed()
        {
            // 1) Check for a running pwsh.exe
            if (Process.GetProcessesByName("pwsh").Length > 0)
                return true;

            // 2) Fallback to probing the filesystem
            var ps7Path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "PowerShell", "7", "pwsh.exe");
            return false;
        }

        // Installs PowerShell 7 after fetching release from GitHub
        private async Task InstallPowerShell7Async()
        {
            try
            {
                // 1) Fetch latest PowerShell 7 asset from GitHub
                _log.Log("Fetching PowerShell 7 release info…", "INFO");
                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("AdvTechSetup");

                var json = await http
                    .GetStringAsync("https://api.github.com/repos/PowerShell/PowerShell/releases/latest");
                using var doc = JsonDocument.Parse(json);

                var asset = doc.RootElement
                    .GetProperty("assets")
                    .EnumerateArray()
                    .Select(a => new {
                        Name = a.GetProperty("name").GetString()!,
                        Url = a.GetProperty("browser_download_url").GetString()!
                    })
                    .FirstOrDefault(x =>
                        x.Name.EndsWith("win-x64.msi", StringComparison.OrdinalIgnoreCase) &&
                        !x.Name.Contains("preview", StringComparison.OrdinalIgnoreCase));

                if (asset == null)
                {
                    _log.Log("Could not locate PowerShell 7 MSI.", "ERROR");
                    return;
                }

                // 2) Download into the .NET single-file extraction folder
                var downloadDir = AppDomain.CurrentDomain.BaseDirectory;
                Directory.CreateDirectory(downloadDir);

                var msiName = Path.GetFileName(new Uri(asset.Url).LocalPath)!;
                var msiPath = Path.Combine(downloadDir, msiName);
                _log.Log($"Downloading PowerShell 7 MSI to {msiPath}…", "INFO");

                using (var resp = await http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead))
                {
                    resp.EnsureSuccessStatusCode();
                    await using var remoteStream = await resp.Content.ReadAsStreamAsync();
                    await using var fileStream = new FileStream(
                        msiPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.Read,          // allow concurrent reads
                        bufferSize: 1 << 20,
                        useAsync: true);

                    await remoteStream.CopyToAsync(fileStream);
                    await fileStream.FlushAsync();
                }

                // 3) Give OS/AV a moment to release locks
                await Task.Delay(2000).ConfigureAwait(false);
                _log.Log("PowerShell 7 MSI download complete.", "INFO");

                // 4) Launch msiexec elevated, logging to file
                var logPath = Path.Combine(downloadDir, "PowerShell7_install.log");
                var msiexec = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "msiexec.exe");

                var args = $"/i \"{msiPath}\" /quiet /norestart /L*V \"{logPath}\"";
                var psi = new ProcessStartInfo(msiexec, args)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    CreateNoWindow = true,
                    WorkingDirectory = downloadDir
                };

                _log.Log("Launching PowerShell 7 installer…", "INFO");
                using var proc = Process.Start(psi)!;
                await proc.WaitForExitAsync().ConfigureAwait(false);

                _log.Log(
                    $"PowerShell 7 installer exited with code {proc.ExitCode}. See log at {logPath}",
                    "INFO");
            }
            catch (Exception ex)
            {
                _log.Log($"InstallPowerShell7Async failed: {ex.Message}", "ERROR");
            }
        }

        // installs DSC 3 by downloading the latest release from GitHub
        public async Task InstallDsc3Async()
        {
            try
            {
                _log.Log("Fetching latest DSC v3 release…", "INFO");

                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("AdvTechSetup");

                // 1) Discover the ZIP URL
                var json = await http
                    .GetStringAsync("https://api.github.com/repos/PowerShell/DSC/releases/latest")
                    .ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);

                var asset = doc.RootElement
                    .GetProperty("assets")
                    .EnumerateArray()
                    .FirstOrDefault(a =>
                        a.GetProperty("name").GetString()!
                         .Contains("x86_64-pc-windows-msvc.zip", StringComparison.OrdinalIgnoreCase));

                if (asset.ValueKind != JsonValueKind.Object)
                {
                    _log.Log("DSC3 ZIP asset not found.", "ERROR");
                    return;
                }

                var zipUrl = asset.GetProperty("browser_download_url").GetString()!;
                var zipPath = Path.Combine(_baseDir, "dsc3.zip");
                if (File.Exists(zipPath)) File.Delete(zipPath);

                // 2) Download into %LOCALAPPDATA%\AdvTechSetup
                _log.Log($"Starting download of DSC3 to {zipPath}…", "INFO");
                await DownloadToFileAsync(http, zipUrl, zipPath).ConfigureAwait(false);
                _log.Log("Download complete.", "INFO");

                // 3) Extract under Program Files
                var installDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "DSC3");
                Directory.CreateDirectory(installDir);

                _log.Log("Extracting DSC…", "INFO");
                ZipFile.ExtractToDirectory(zipPath, installDir, overwriteFiles: true);

                // 4) Add to machine PATH
                var machinePath = Environment.GetEnvironmentVariable(
                    "Path", EnvironmentVariableTarget.Machine) ?? "";

                if (!machinePath.Contains(installDir, StringComparison.OrdinalIgnoreCase))
                {
                    _log.Log("Adding DSC3 to machine PATH…", "INFO");
                    Environment.SetEnvironmentVariable(
                        "Path",
                        $"{machinePath};{installDir}",
                        EnvironmentVariableTarget.Machine);
                }

                _log.Log("DSC3 installation complete.", "INFO");
            }
            catch (UnauthorizedAccessException)
            {
                _log.Log("Permission denied: please run the setup as Administrator.", "ERROR");
            }
            catch (Exception ex)
            {
                _log.Log($"InstallDsc3Async failed: {ex.Message}", "ERROR");
            }
        }

        // Runs a PowerShell command asynchronously, capturing output and errors
        private static Task<int> RunPwshAsync(
            string command,
            Action<string> onOutput,
            Action<string> onError,
            bool useShellExecute = false,
            string? verb = null)
        {
            // 10.1) Locate the PS7 runtime folder in Program Files
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pwshPath = Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe");

            if (!File.Exists(pwshPath))
                throw new FileNotFoundException("PowerShell 7 not found at expected location.", pwshPath);

            // 10.2) Build invocation arguments
            var args = $"-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -Command \"{command}\"";

            // 10.3) Delegate to your existing process-runner
            return RunProcessAsync(
                pwshPath,
                args,
                onOutput,
                onError ?? (_ => { }),
                useShellExecute: useShellExecute,
                verb: verb
            );
        }

        // Downloads a file from the given URL to the specified destination path
        private static async Task DownloadToFileAsync(HttpClient http, string url, string destination)
        {
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead)
                                      .ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            await using var remote = await resp.Content.ReadAsStreamAsync()
                                                     .ConfigureAwait(false);
            await using var file = new FileStream(
                destination,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1 << 20,
                useAsync: true);

            await remote.CopyToAsync(file).ConfigureAwait(false);
        }

        // Runs a process asynchronously, capturing output and errors
        private static Task<int> RunProcessAsync(
            string exePath,
            string args,
            Action<string> onOutput,
            Action<string> onError,
            bool useShellExecute = false,
            string? verb = null)
        {
            if (!File.Exists(exePath))
                throw new FileNotFoundException("Executable not found", exePath);

            var tcs = new TaskCompletionSource<int>();
            var psi = new ProcessStartInfo(exePath, args)
            {
                UseShellExecute = useShellExecute,
                Verb = verb,
                RedirectStandardOutput = !useShellExecute,
                RedirectStandardError = !useShellExecute,
                CreateNoWindow = true
            };

            var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            if (!useShellExecute)
            {
                proc.OutputDataReceived += (_, e) => { if (e.Data != null) onOutput(e.Data); };
                proc.ErrorDataReceived += (_, e) => { if (e.Data != null) onError(e.Data); };
            }

            proc.Exited += (_, _) =>
            {
                tcs.TrySetResult(proc.ExitCode);
                proc.Dispose();
            };

            proc.Start();
            if (!useShellExecute)
            {
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
            }

            return tcs.Task;
        }
    }
}