namespace DepPilot.Master.Services;

public sealed class MasterOptions
{
    public string AdminUser { get; set; } = "admin";
    public string AdminPassword { get; set; } = "";
    public string BootstrapToken { get; set; } = "";
    public string DatabasePath { get; set; } = "data/dep-pilot.db";
    public bool AllowInsecureHttp { get; set; }
    public int OfflineAfterSeconds { get; set; } = 60;
    public int LeaseSeconds { get; set; } = 45;
    public int JobTimeoutSeconds { get; set; } = 600;
}
