using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Core.Security;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace Coflnet.Sky.McConnect;

/// <summary>
/// Optional MariaDB client certificate support, same keys as the other sky services:
/// DB__TLS__CA_CERT_PATH, DB__TLS__CERT_PATH and DB__TLS__KEY_PATH.
/// Without the DB:TLS section the connection string is used unchanged (password only).
/// </summary>
internal static class DatabaseTls
{
    private static readonly string[] RequiredKeys = { "CA_CERT_PATH", "CERT_PATH", "KEY_PATH" };

    internal static bool IsEnabled(IConfiguration configuration) => configuration.GetSection("DB:TLS").Exists();

    internal static string ConnectionString(IConfiguration configuration, string key)
    {
        var configured = configuration[key] ?? throw new InvalidOperationException($"{key} must be configured.");
        if (!IsEnabled(configuration))
            return configured;
        EnsureComplete(configuration.GetSection("DB:TLS"));
        var options = new MySqlConnectionStringBuilder(configured);
        options.ConnectionTimeout = options.ConnectionTimeout == 0 ? 10 : Math.Min(options.ConnectionTimeout, 10);
        options.ConnectionLifeTime = options.ConnectionLifeTime == 0 ? 300 : Math.Min(options.ConnectionLifeTime, 300);
        // MySqlConnector can soften VerifyFull failures with MariaDB password-bound TLS.
        // Required + the strict validation callback rejects CA/name errors before authentication.
        options.SslMode = MySqlSslMode.Required;
        options.SslCa = options.SslCert = options.SslKey = "";
        // Native PEM/PFX/store identities override the physical-connection provider.
        options.CertificateFile = options.CertificatePassword = options.CertificateThumbprint = "";
        options.CertificateStoreLocation = MySqlCertificateStoreLocation.None;
        return options.ConnectionString;
    }

    private static void EnsureComplete(IConfigurationSection section)
    {
        foreach (var key in RequiredKeys)
            if (string.IsNullOrWhiteSpace(section[key]))
                throw new InvalidOperationException($"DB:TLS:{key} must be configured.");
    }
}

internal sealed class DatabaseTlsInterceptor(PemTlsOptions credentials) : DbConnectionInterceptor
{
    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        Prepare(connection);
        return result;
    }

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
        InterceptionResult result, CancellationToken cancellationToken = default)
    {
        Prepare(connection);
        return new(result);
    }

    private void Prepare(DbConnection connection)
    {
        var mysql = (MySqlConnection)connection;
        mysql.RemoteCertificateValidationCallback = credentials.ValidateServerCertificate;
        mysql.ProvideClientCertificatesCallback = certificates =>
        {
            credentials.RegisterClientIntermediates();
            certificates.Add(credentials.GetCertificate());
            return ValueTask.CompletedTask;
        };
    }
}
