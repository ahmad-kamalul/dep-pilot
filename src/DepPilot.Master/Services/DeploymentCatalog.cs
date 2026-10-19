using System.Text.Json;
using DepPilot.Master.Data;
using DepPilot.Master.Security;
using Microsoft.EntityFrameworkCore;

namespace DepPilot.Master.Services;

public sealed record DeploymentPackage(string Name, string Version, string Playbook, string[] Prerequisites);

public static class DeploymentCatalog
{
    public static async Task SeedAsync(RegistryDb db, string directory)
    {
        foreach (var file in Directory.GetFiles(Path.Combine(directory, "packages"), "*.json"))
        {
            var package = JsonSerializer.Deserialize<DeploymentPackage>(await File.ReadAllTextAsync(file), new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException("Invalid deployment package.");
            if (string.IsNullOrWhiteSpace(package.Name) || !System.Version.TryParse(package.Version, out _)
                || string.IsNullOrEmpty(package.Playbook) || Path.GetFileName(package.Playbook) != package.Playbook || !package.Playbook.EndsWith(".yml", StringComparison.Ordinal))
                throw new InvalidOperationException("Package requires a name, numeric version, and local YAML filename.");
            var content = await File.ReadAllTextAsync(Path.Combine(directory, "playbooks", package.Playbook));
            var hash = TokenSecurity.Hash(content);
            var existing = await db.Playbooks.SingleOrDefaultAsync(x => x.Name == package.Name && x.Version == package.Version);
            if (existing is null) db.Playbooks.Add(new() { Name = package.Name, Version = package.Version, Content = content, Sha256 = hash });
            else if (existing.Sha256 != hash) throw new InvalidOperationException("Playbook content changed without a version increment.");
        }
        await db.SaveChangesAsync();
    }
}
