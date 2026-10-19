using System.Security.Cryptography;
using System.Text.Json;
using DepPilot.Contracts;

namespace DepPilot.Agent;

public sealed record AgentIdentity(Guid InstallationId, string Token, Guid? HostId);
public sealed record PendingResult(Guid JobId, CompletionRequest Completion, string Output);

public sealed class AgentState
{
    private readonly string directory;
    public AgentIdentity Identity { get; private set; }
    public AgentState(AgentOptions options)
    {
        directory = Path.GetFullPath(options.StateDirectory);
        if (File.Exists(Path.Combine(directory, "deregistered.json"))) throw new InvalidOperationException("This Agent was deregistered. Provision a new installation deliberately before restarting it.");
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var path = Path.Combine(directory, "identity.json");
        Identity = File.Exists(path) ? JsonSerializer.Deserialize<AgentIdentity>(File.ReadAllText(path))! : new(Guid.NewGuid(), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), null);
        if (Identity is null || Identity.InstallationId == Guid.Empty || Identity.Token.Length != 64) throw new InvalidOperationException("Invalid Agent identity file. Restore it from backup; do not silently enroll a new host.");
        Write("identity.json", Identity);
    }
    public void SetHost(Guid id) { Identity = Identity with { HostId = id }; Write("identity.json", Identity); }
    public void SaveResult(PendingResult result) => Write("pending-result.json", result);
    public PendingResult? LoadResult() => File.Exists(Path.Combine(directory, "pending-result.json")) ? JsonSerializer.Deserialize<PendingResult>(File.ReadAllText(Path.Combine(directory, "pending-result.json"))) : null;
    public void ClearResult() => File.Delete(Path.Combine(directory, "pending-result.json"));
    public void QuarantineResult() => File.Move(Path.Combine(directory, "pending-result.json"), Path.Combine(directory, $"rejected-result-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.json"), true);
    public string CreateJobDirectory(Guid jobId)
    {
        var path = Path.Combine(directory, "jobs", jobId.ToString("N"));
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
    private void Write<T>(string file, T value)
    {
        var path = Path.Combine(directory, file);
        var temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            JsonSerializer.Serialize(stream, value);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, true);
    }
}
