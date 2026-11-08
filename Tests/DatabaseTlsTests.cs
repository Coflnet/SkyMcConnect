using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Coflnet.Core.Security;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using NUnit.Framework;

namespace Coflnet.Sky.McConnect;

public class DatabaseTlsTests
{
    private const string Legacy = "Server=legacy;User ID=svc;Password=synthetic-only;Database=svc;SslMode=None";

    [Test]
    public void WithoutTlsKeysConnectionStringIsUnchanged()
    {
        var configuration = Configuration(new());
        Assert.That(DatabaseTls.IsEnabled(configuration), Is.False);
        Assert.That(DatabaseTls.ConnectionString(configuration, "DB_CONNECTION"), Is.EqualTo(Legacy));
    }

    [Test]
    public void WithTlsKeysTlsOptionsAreAppliedAndPasswordIsKept()
    {
        var configuration = Configuration(new()
        {
            ["DB:TLS:CA_CERT_PATH"] = "roots.pem",
            ["DB:TLS:CERT_PATH"] = "client.pem",
            ["DB:TLS:KEY_PATH"] = "client.key",
        });
        var options = new MySqlConnectionStringBuilder(DatabaseTls.ConnectionString(configuration, "DB_CONNECTION"));
        Assert.Multiple(() =>
        {
            Assert.That(DatabaseTls.IsEnabled(configuration), Is.True);
            Assert.That(options.SslMode, Is.EqualTo(MySqlSslMode.Required));
            Assert.That(options.Password, Is.EqualTo("synthetic-only"));
            Assert.That(options.UserID, Is.EqualTo("svc"));
            Assert.That(options.ConnectionTimeout, Is.EqualTo(10));
            Assert.That(options.ConnectionLifeTime, Is.EqualTo(300));
            Assert.That(options.SslCa, Is.Empty);
        });
    }

    [TestCase("CA_CERT_PATH")]
    [TestCase("CERT_PATH")]
    [TestCase("KEY_PATH")]
    public void PartialTlsConfigurationFailsClosed(string missing)
    {
        var values = new Dictionary<string, string>
        {
            ["DB:TLS:CA_CERT_PATH"] = "roots.pem",
            ["DB:TLS:CERT_PATH"] = "client.pem",
            ["DB:TLS:KEY_PATH"] = "client.key",
        };
        values.Remove("DB:TLS:" + missing);
        Assert.Throws<InvalidOperationException>(() => DatabaseTls.ConnectionString(Configuration(values), "DB_CONNECTION"));
    }

    [Test]
    public async Task InterceptorSetsCallbacksAndMissingCertificateFailsClosed()
    {
        using var owner = new PemTlsOptions("missing-roots", "missing-cert", "missing-key");
        var interceptor = new DatabaseTlsInterceptor(owner);
        using var connection = new MySqlConnection();
        Assert.DoesNotThrow(() => interceptor.ConnectionOpening(connection, null, default));
        Assert.That(connection.RemoteCertificateValidationCallback, Is.Not.Null);
        Assert.That(connection.ProvideClientCertificatesCallback, Is.Not.Null);
        Assert.Throws<FileNotFoundException>(() => connection.ProvideClientCertificatesCallback(new()));
        connection.RemoteCertificateValidationCallback = null;
        connection.ProvideClientCertificatesCallback = null;
        await interceptor.ConnectionOpeningAsync(connection, null, default);
        Assert.That(connection.RemoteCertificateValidationCallback, Is.Not.Null);
        Assert.Throws<FileNotFoundException>(() => connection.ProvideClientCertificatesCallback(new()));
    }

    private static IConfiguration Configuration(Dictionary<string, string> values)
    {
        values.TryAdd("DB_CONNECTION", Legacy);
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
