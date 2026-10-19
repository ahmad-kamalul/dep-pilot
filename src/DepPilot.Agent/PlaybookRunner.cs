using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DepPilot.Contracts;

namespace DepPilot.Agent;

public sealed record ExecutionResult(int? ExitCode, bool TimedOut, string Output);

public static class OutputSanitizer
{
    public static string Redact(string text, params string[] secrets)
    {
        foreach (var secret in secrets.Where(s => !string.IsNullOrEmpty(s)))
        {
            text = text.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
            var escaped = JsonSerializer.Serialize(secret)[1..^1];
            text = text.Replace(escaped, "[REDACTED]", StringComparison.Ordinal);
        }
        return text;
    }
}

public sealed class PlaybookRunner(AgentOptions options, AgentState state)
{
    public async Task<ExecutionResult> RunAsync(JobAssignment job, string content, CancellationToken ct)
    {
        var actualHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        if (!string.Equals(actualHash, job.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Playbook integrity verification failed.");
        if (job.TimeoutSeconds is < 1 or > 3600) throw new InvalidOperationException("Invalid execution timeout.");
        var directory = state.CreateJobDirectory(job.JobId);
        var playbookPath = Path.Combine(directory, "playbook.yml");
        var variablesPath = Path.Combine(directory, "variables.json");
        await File.WriteAllTextAsync(playbookPath, content, new UTF8Encoding(false), ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            var variables = new
            {
                minio_name = "dep-pilot-minio-" + state.Identity.HostId!.Value.ToString("N"),
                minio_root_user = options.MinioRootUser,
                minio_root_password = options.MinioRootPassword,
                minio_api_port = options.MinioApiPort,
                minio_console_port = options.MinioConsolePort,
                minio_bind_address = options.MinioBindAddress,
                minio_probe_host = options.MinioProbeHost,
                minio_storage_path = options.MinioStoragePath
            };
            // Create with restricted permissions before writing credentials.
            using (var stream = new FileStream(variablesPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(variablesPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                await JsonSerializer.SerializeAsync(stream, variables, cancellationToken: ct);
            }
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(job.TimeoutSeconds));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
            var useWsl = OperatingSystem.IsWindows() && options.UseWsl;
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo(useWsl ? "wsl.exe" : options.AnsibleExecutable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = directory
            };
            if (useWsl)
                foreach (var argument in new[] { "--distribution", options.WslDistribution, "--exec", "env", "ANSIBLE_LOCAL_TEMP=" + WslPath(Path.Combine(directory, "tmp")), "ANSIBLE_NOCOLOR=1", options.AnsibleExecutable })
                    process.StartInfo.ArgumentList.Add(argument);
            foreach (var argument in new[] { "-i", "localhost,", "--connection", "local", "--extra-vars", "@" + (useWsl ? WslPath(variablesPath) : variablesPath), useWsl ? WslPath(playbookPath) : playbookPath }) process.StartInfo.ArgumentList.Add(argument);
            process.StartInfo.Environment["ANSIBLE_NOCOLOR"] = "1";
            process.StartInfo.Environment["ANSIBLE_HOST_KEY_CHECKING"] = "True";
            process.StartInfo.Environment["ANSIBLE_LOCAL_TEMP"] = Path.Combine(directory, "tmp");
            // Ansible needs the Docker endpoint for the local Compose simulation.
            var output = new StringBuilder();
            var sync = new object();
            const int maxOutput = 256000;
            async Task ReadAsync(StreamReader reader)
            {
                var buffer = new char[2048];
                int count;
                while ((count = await reader.ReadAsync(buffer)) > 0)
                {
                    lock (sync) { if (output.Length < maxOutput) output.Append(buffer, 0, Math.Min(count, maxOutput - output.Length)); }
                }
            }
            ct.ThrowIfCancellationRequested();
            process.Start();
            var stdout = ReadAsync(process.StandardOutput);
            var stderr = ReadAsync(process.StandardError);
            var timedOut = false;
            try { await process.WaitForExitAsync(linked.Token); }
            catch (OperationCanceledException)
            {
                timedOut = deadline.IsCancellationRequested;
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
            await Task.WhenAll(stdout, stderr);
            var text = OutputSanitizer.Redact(output.ToString(), options.MinioRootPassword, options.BootstrapToken, state.Identity.Token);
            if (output.Length >= maxOutput) text += "\n[Output truncated to 256000 characters]";
            return new(process.ExitCode, timedOut, text);
        }
        finally
        {
            File.Delete(variablesPath);
            // Ansible temp files may contain module arguments; keep the execution directory private.
            var temp = Path.Combine(directory, "tmp");
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }

    public static string WslPath(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.Length < 3 || full[1] != ':' || full[2] != '\\') throw new InvalidOperationException("Windows Agent state must use a local drive path accessible from WSL.");
        return "/mnt/" + char.ToLowerInvariant(full[0]) + full[2..].Replace('\\', '/');
    }
}
