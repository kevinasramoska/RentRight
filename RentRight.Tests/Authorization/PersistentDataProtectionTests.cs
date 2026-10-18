using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using RentRight.Security;
using Xunit;

namespace RentRight.Tests.Authorization;

public sealed class PersistentDataProtectionTests : IDisposable
{
    private const string Prefix = "Security:DataProtection:";
    private readonly DirectoryInfo _temporary = Directory.CreateTempSubdirectory("RentRight-key-tests-");

    [Fact]
    public void Protection_EncryptedKeyRingAcrossProviderRestart_RecoversPayloadAndIsolatesApplications()
    {
        var settings = Settings();
        settings[Prefix + "CertificatePath"] = CreateCertificate();
        settings[Prefix + "CertificatePassword"] = "test-only-certificate-password";
        string protectedValue;
        using (var first = CreateServices(settings))
        {
            protectedValue = first.GetRequiredService<IDataProtectionProvider>().CreateProtector("cookie-test")
                .Protect("synthetic-cookie-payload");
        }
        var key = File.ReadAllText(Assert.Single(Directory.GetFiles(settings[Prefix + "KeyRingPath"]!, "key-*.xml")));
        Assert.Contains("encryptedSecret", key);
        Assert.DoesNotContain("<masterKey", key);

        using (var restarted = CreateServices(settings))
        {
            var protector = restarted.GetRequiredService<IDataProtectionProvider>().CreateProtector("cookie-test");
            Assert.Equal("synthetic-cookie-payload", protector.Unprotect(protectedValue));
        }
        settings[Prefix + "ApplicationName"] = "RentRight-independent-environment";
        using var isolated = CreateServices(settings);
        Assert.Throws<CryptographicException>(() => isolated.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("cookie-test").Unprotect(protectedValue));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Protection_PersistentKeysOutsideDevelopmentWithoutCertificate_FailsClosed(string environment)
    {
        var error = Assert.Throws<InvalidOperationException>(() => CreateServices(Settings(), environment));
        Assert.Contains("require a certificate", error.Message);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void Protection_LocalExplicitKeyRingWithoutCertificate_SurvivesRestart(string environment)
    {
        var settings = Settings();
        string payload;
        using (var first = CreateServices(settings, environment))
            payload = first.GetRequiredService<IDataProtectionProvider>().CreateProtector("local-test").Protect("synthetic");
        using var restarted = CreateServices(settings, environment);
        Assert.Equal("synthetic", restarted.GetRequiredService<IDataProtectionProvider>().CreateProtector("local-test").Unprotect(payload));
    }

    [Theory]
    [InlineData("relative-directory")]
    [InlineData("blank-directory")]
    [InlineData("missing-directory")]
    [InlineData("checkout-directory")]
    [InlineData("missing-application")]
    [InlineData("certificate-without-directory")]
    [InlineData("password-without-directory")]
    [InlineData("relative-certificate")]
    [InlineData("checkout-certificate")]
    [InlineData("missing-certificate")]
    public void Configuration_UnsafeOrIncompleteLocation_IsRejected(string scenario)
    {
        var settings = Settings();
        switch (scenario)
        {
            case "relative-directory": settings[Prefix + "KeyRingPath"] = "keys"; break;
            case "blank-directory": settings[Prefix + "KeyRingPath"] = " "; break;
            case "missing-directory": settings[Prefix + "KeyRingPath"] = Path.Combine(_temporary.FullName, "not-created"); break;
            case "checkout-directory": settings[Prefix + "KeyRingPath"] = ContentRoot; break;
            case "missing-application": settings.Remove(Prefix + "ApplicationName"); break;
            case "certificate-without-directory": settings.Clear(); settings[Prefix + "CertificatePath"] = "/not-a-real-certificate.pfx"; break;
            case "password-without-directory": settings.Clear(); settings[Prefix + "CertificatePassword"] = "not-a-real-secret"; break;
            case "relative-certificate": settings[Prefix + "CertificatePath"] = "certificate.pfx"; break;
            case "checkout-certificate": settings[Prefix + "CertificatePath"] = Path.Combine(ContentRoot, "certificate.pfx"); break;
            case "missing-certificate": settings[Prefix + "CertificatePath"] = Path.Combine(_temporary.FullName, "not-created.pfx"); break;
        }
        Assert.Throws<InvalidOperationException>(() => CreateServices(settings));
    }

    [Theory]
    [InlineData("wrong-password")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("no-private-key")]
    [InlineData("weak-rsa")]
    [InlineData("ecdsa")]
    public void Certificate_InvalidOrNotCurrentlyUsable_IsRejectedWithoutEchoingSecret(string scenario)
    {
        var settings = Settings();
        settings[Prefix + "CertificatePath"] = CreateCertificate(scenario);
        settings[Prefix + "CertificatePassword"] = scenario == "wrong-password" ? "private-wrong-password" : "test-only-certificate-password";
        var error = Assert.Throws<InvalidOperationException>(() => CreateServices(settings));
        Assert.DoesNotContain(settings[Prefix + "CertificatePassword"]!, error.Message);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void Protection_NoExplicitStore_PreservesPlatformDefaultUntilDeploymentIsProvisioned()
    {
        using var provider = CreateServices([], "Production");
        Assert.NotNull(provider.GetRequiredService<IDataProtectionProvider>());
    }

    private string ContentRoot => Path.Combine(_temporary.FullName, "checkout");

    private Dictionary<string, string?> Settings()
    {
        Directory.CreateDirectory(ContentRoot);
        var directory = Directory.CreateDirectory(Path.Combine(_temporary.FullName, "key-ring"));
        return new()
        {
            [Prefix + "KeyRingPath"] = directory.FullName,
            [Prefix + "ApplicationName"] = "RentRight-isolated-test"
        };
    }

    private ServiceProvider CreateServices(Dictionary<string, string?> settings, string environment = "Production")
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistentDataProtection(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            new TestEnvironment { ContentRootPath = ContentRoot, EnvironmentName = environment });
        return services.BuildServiceProvider();
    }

    private string CreateCertificate(string scenario = "valid")
    {
        using var rsa = RSA.Create(scenario == "weak-rsa" ? 1024 : 2048);
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = scenario == "ecdsa"
            ? new CertificateRequest("CN=RentRight-disposable-test", ecdsa, HashAlgorithmName.SHA256)
            : new CertificateRequest("CN=RentRight-disposable-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var now = DateTimeOffset.UtcNow;
        using var certificate = request.CreateSelfSigned(
            scenario == "future" ? now.AddDays(1) : now.AddDays(-2),
            scenario == "expired" ? now.AddDays(-1) : now.AddDays(2));
        using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));
        var path = Path.Combine(_temporary.FullName, "certificate.pfx");
        File.WriteAllBytes(path, (scenario == "no-private-key" ? publicOnly : certificate)
            .Export(X509ContentType.Pkcs12, "test-only-certificate-password"));
        return path;
    }

    public void Dispose() => _temporary.Delete(recursive: true);

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "RentRight";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
