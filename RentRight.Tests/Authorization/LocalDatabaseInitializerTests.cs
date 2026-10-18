using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using RentRight.Data;
using Xunit;

namespace RentRight.Tests.Authorization;

public sealed class LocalDatabaseInitializerTests
{
    [Fact]
    public void ValidateTarget_ExplicitDevelopmentComposeDatabase_IsAllowed()
    {
        LocalDatabaseInitializer.ValidateTarget(new Environment("Development"), Settings());
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public void ValidateTarget_NonDevelopmentEnvironment_IsRejectedBeforeDatabaseAccess(string environment)
    {
        Assert.Throws<InvalidOperationException>(() => LocalDatabaseInitializer.ValidateTarget(new Environment(environment), Settings()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public void ValidateTarget_NoExplicitPermission_IsRejected(string? permission)
    {
        var settings = Settings();
        settings["LocalDevelopment:AllowInitialization"] = permission;
        Assert.Throws<InvalidOperationException>(() => LocalDatabaseInitializer.ValidateTarget(new Environment("Development"), settings));
    }

    [Theory]
    [InlineData("Server=localhost,1433;Database=RentRightLocal")]
    [InlineData("Server=sql,1433;Database=RentRightDB")]
    [InlineData("Server=shared.example.test,1433;Database=RentRightLocal")]
    [InlineData("Server=sql,1444;Database=RentRightLocal")]
    [InlineData("Server=tcp:sql,1433;Database=RentRightLocal")]
    [InlineData("not a connection string")]
    [InlineData("")]
    public void ValidateTarget_UnexpectedOrMalformedDatabase_IsRejectedWithoutEchoingSecrets(string connection)
    {
        var settings = Settings();
        settings["ConnectionStrings:DefaultConnection"] = connection + ";Password=synthetic-private-value";
        var error = Assert.Throws<InvalidOperationException>(() => LocalDatabaseInitializer.ValidateTarget(new Environment("Development"), settings));
        Assert.DoesNotContain("synthetic-private-value", error.Message);
        Assert.Null(error.InnerException);
    }

    private static IConfigurationRoot Settings() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["LocalDevelopment:AllowInitialization"] = "true",
        ["ConnectionStrings:DefaultConnection"] = "Server=sql,1433;Database=RentRightLocal;User ID=sa;Password=synthetic-private-value"
    }).Build();

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "RentRight";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
