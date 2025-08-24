using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using WS_Setup_6.Common.Interfaces;
using WS_Setup_6.Common.Logging;
using WS_Setup_6.Core.Interfaces;

namespace WS_Setup_6.Core.Services
{
    [SupportedOSPlatform("windows")]
    public class BaselineService : IBaselineService
    {
        private readonly ILogService _log;

        public BaselineService(ILogService log)
        {
            _log = log;
        }

        /// <summary>
        /// Decrypts the given input file into the output path using AES.
        /// </summary>
        public void DecryptConfig(
            string inFile,
            string outFile,
            byte[] key,
            byte[] iv)
        {
            using var aes = System.Security.Cryptography.Aes.Create();
            aes.Key = key;
            aes.IV = iv;

            var data = File.ReadAllBytes(inFile);
            var plain = aes.CreateDecryptor()
                           .TransformFinalBlock(data, 0, data.Length);

            File.WriteAllBytes(outFile, plain);
            _log.Log($"Decrypted configuration to {outFile}", "INFO");
        }

        /// <summary>
        /// Executes DSC in “set” mode against the given YAML config.
        /// All output and errors are shipped through ILogService.
        /// </summary>
        public Task RunDscSimpleAsync(string yamlPath)
        {
            _log.Log("Configuring baseline via DSC", "INFO");

            // locate DSC.exe and pwsh.exe
            var programFiles = Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles);
            var dscExe = Path.Combine(programFiles, "DSC3", "DSC.exe");
            var pwsh = Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe");

            if (!File.Exists(dscExe) || !File.Exists(pwsh))
            {
                _log.Log(
                    $"Missing DSC.exe ({dscExe}) or pwsh.exe ({pwsh})",
                    "ERROR");
                return Task.CompletedTask;
            }

            // build the process start info
            var psi = new ProcessStartInfo(dscExe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.Environment["DSC_HOST_PATH"] = pwsh;
            psi.Environment["PATH"] =
                Path.GetDirectoryName(pwsh)! + ";" +
                psi.Environment["PATH"];
            psi.ArgumentList.Add("config");
            psi.ArgumentList.Add("set");
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add(yamlPath);

            // run DSC asynchronously and hook output
            return Task.Run(() =>
            {
                using var proc = new Process
                {
                    StartInfo = psi,
                    EnableRaisingEvents = true
                };

                proc.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        _log.Log(e.Data, "DEBUG");
                };
                proc.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        _log.Log(e.Data, "ERROR");
                };

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                proc.WaitForExit();

                if (proc.ExitCode != 0)
                {
                    _log.Log($"DSC exited with code {proc.ExitCode}", "ERROR");
                }
                else
                {
                    _log.Log("Baseline configuration applied successfully", "SUMMARY");
                }
            });
        }
    }
}