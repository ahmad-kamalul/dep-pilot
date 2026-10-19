namespace DepPilot.Agent;

public sealed class AgentOptions
{
    public string MasterUrl { get; set; } = "https://localhost:7443";
    public string BootstrapToken { get; set; } = "";
    public bool AllowInsecureHttp { get; set; }
    public string StateDirectory { get; set; } = "state";
    public string Hostname { get; set; } = Environment.MachineName;
    public int PollSeconds { get; set; } = 5;
    public int HeartbeatSeconds { get; set; } = 15;
    public string AnsibleExecutable { get; set; } = "ansible-playbook";
    public string MinioRootUser { get; set; } = "dep-pilot";
    public string MinioRootPassword { get; set; } = "";
    public int MinioApiPort { get; set; } = 9000;
    public int MinioConsolePort { get; set; } = 9001;
    public string MinioBindAddress { get; set; } = "127.0.0.1";
    public string MinioProbeHost { get; set; } = "127.0.0.1";
}
