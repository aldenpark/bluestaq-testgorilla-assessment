using System.Text.Json;
using Bluestaq.Notes.Api.Administration;
using Bluestaq.Notes.Api.Data;
using Bluestaq.Notes.Api.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bluestaq.Notes.Api.Tests;

[Trait("Category", "Integration")]
public sealed class AdministrativeCommandTests
{
    [Fact]
    public async Task BootstrapRecoveryAndAuditVerification_AreOutOfBandAndTamperEvident()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"bluestaq-admin-{Guid.NewGuid():N}.db");
        try
        {
            await using var provider = BuildProvider(databasePath);
            await using (var scope = provider.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<NotesDbContext>().Database.MigrateAsync();
            }

            var bootstrapOutput = new StringWriter();
            Assert.Equal(0, await AdministrativeCommandRunner.TryRunAsync(
                ["bootstrap", "--display-name", "Administrator", "--key-name", "Initial"],
                provider,
                bootstrapOutput,
                TextWriter.Null));
            using var bootstrapJson = JsonDocument.Parse(bootstrapOutput.ToString());
            var userId = bootstrapJson.RootElement.GetProperty("UserId").GetGuid();
            Assert.StartsWith("bns_", bootstrapJson.RootElement.GetProperty("ApiKey").GetString());

            Assert.Equal(1, await AdministrativeCommandRunner.TryRunAsync(
                ["bootstrap", "--display-name", "Second", "--key-name", "Denied"],
                provider,
                TextWriter.Null,
                TextWriter.Null));

            var recoveryOutput = new StringWriter();
            var recoveryError = new StringWriter();
            var recoveryResult = await AdministrativeCommandRunner.TryRunAsync(
                ["recover-credential", "--user-id", userId.ToString(), "--key-name", "Recovered"],
                provider,
                recoveryOutput,
                recoveryError);
            Assert.True(recoveryResult == 0, recoveryError.ToString());
            using var recoveryJson = JsonDocument.Parse(recoveryOutput.ToString());
            Assert.StartsWith("bns_", recoveryJson.RootElement.GetProperty("ApiKey").GetString());
            Assert.Equal(0, await AdministrativeCommandRunner.TryRunAsync(
                ["verify-audit"], provider, TextWriter.Null, TextWriter.Null));
            var pruneBefore = DateTime.UtcNow.AddMinutes(1).ToString("O");
            Assert.Equal(0, await AdministrativeCommandRunner.TryRunAsync(
                ["prune-audit", "--before", pruneBefore],
                provider,
                TextWriter.Null,
                TextWriter.Null));
            Assert.Equal(0, await AdministrativeCommandRunner.TryRunAsync(
                ["prune-audit", "--before", pruneBefore, "--confirm"],
                provider,
                TextWriter.Null,
                TextWriter.Null));
            Assert.Equal(0, await AdministrativeCommandRunner.TryRunAsync(
                ["verify-audit"], provider, TextWriter.Null, TextWriter.Null));

            await using (var scope = provider.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<NotesDbContext>();
                await dbContext.Database.ExecuteSqlRawAsync(
                    "DROP TRIGGER \"SecurityAuditEvents_NoUpdate\"; " +
                    "UPDATE \"SecurityAuditEvents\" SET \"Action\" = 'tampered' WHERE rowid = " +
                    "(SELECT rowid FROM \"SecurityAuditEvents\" LIMIT 1);");
            }

            Assert.Equal(1, await AdministrativeCommandRunner.TryRunAsync(
                ["verify-audit"], provider, TextWriter.Null, TextWriter.Null));
        }
        finally
        {
            foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    private static ServiceProvider BuildProvider(string databasePath)
    {
        var services = new ServiceCollection();
        services.AddDbContext<NotesDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IOptions<HardeningOptions>>(Options.Create(new HardeningOptions
        {
            AuditIntegrityKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=",
        }));
        services.AddSingleton<SecurityAuditIntegrity>();
        return services.BuildServiceProvider();
    }
}
