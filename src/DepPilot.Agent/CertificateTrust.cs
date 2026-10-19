using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace DepPilot.Agent;

public static class CertificateTrust
{
    public static HttpClientHandler Handler(string certificateFile)
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        if (string.IsNullOrEmpty(certificateFile)) return handler;
        var trusted = X509Certificate2.CreateFromPem(File.ReadAllText(certificateFile));
        handler.ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
        {
            if (certificate is null || (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != 0) return false;
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(trusted);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck; // Offline local-demo certificate; normal CA mode uses platform defaults.
            chain.ChainPolicy.ApplicationPolicy.Add(new("1.3.6.1.5.5.7.3.1"));
            return chain.Build(certificate);
        };
        return handler;
    }
}
