using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;

namespace RentRight.Security;

public static class PersistentDataProtection
{
    public static IServiceCollection AddPersistentDataProtection(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection("Security:DataProtection");
        var directory = section["KeyRingPath"];
        var certificatePath = section["CertificatePath"];
        var applicationName = section["ApplicationName"];
        if (directory is not null && string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("An explicitly configured KeyRingPath must not be blank.");
        if (string.IsNullOrWhiteSpace(certificatePath) && !string.IsNullOrEmpty(section["CertificatePassword"]))
            throw new InvalidOperationException("CertificatePassword requires CertificatePath.");
        if (string.IsNullOrWhiteSpace(directory))
        {
            if (!string.IsNullOrWhiteSpace(certificatePath) || !string.IsNullOrWhiteSpace(applicationName) ||
                !string.IsNullOrEmpty(section["CertificatePassword"]))
                throw new InvalidOperationException("Persistent data protection requires KeyRingPath.");
            // Keep local/platform defaults until an explicit deployment key store is provisioned.
            services.AddDataProtection();
            return services;
        }

        if (!IsExternalPath(directory, environment.ContentRootPath) || !Directory.Exists(directory) ||
            string.IsNullOrWhiteSpace(applicationName))
            throw new InvalidOperationException(
                "Data protection requires an existing absolute key directory outside the checkout and an ApplicationName.");

        var protection = services.AddDataProtection().SetApplicationName(applicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(directory));
        if (string.IsNullOrWhiteSpace(certificatePath))
        {
            if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
                throw new InvalidOperationException("Persistent data-protection keys require a certificate outside Development/Testing.");
            return services;
        }

        if (!IsExternalPath(certificatePath, environment.ContentRootPath) || !File.Exists(certificatePath))
            throw new InvalidOperationException("The data-protection certificate must be an existing absolute file outside the checkout.");

        X509Certificate2 certificate;
        try
        {
            certificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath, section["CertificatePassword"],
                // macOS imports into a temporary keychain; EphemeralKeySet is unsupported.
                OperatingSystem.IsMacOS() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Do not echo certificate passwords, configuration values or provider diagnostics.
            throw new InvalidOperationException("Unable to load the data-protection certificate.");
        }
        using var rsa = certificate.GetRSAPublicKey();
        if (!certificate.HasPrivateKey || rsa is null || rsa.KeySize < 2048 || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow ||
            certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow)
        {
            certificate.Dispose();
            throw new InvalidOperationException("Data protection requires a currently valid RSA certificate (at least 2048 bits) with a private key.");
        }
        protection.ProtectKeysWithCertificate(certificate);
        return services;
    }

    private static bool IsExternalPath(string path, string contentRoot)
    {
        if (!Path.IsPathFullyQualified(path)) return false;
        var relative = Path.GetRelativePath(Path.GetFullPath(contentRoot), Path.GetFullPath(path));
        return relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.IsPathFullyQualified(relative);
    }
}
