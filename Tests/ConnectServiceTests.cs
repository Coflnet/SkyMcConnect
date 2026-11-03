using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.McConnect.Models;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Coflnet.Sky.McConnect;

public class ConnectServiceTests
{
    private string databasePath;
    private DbContextOptions<ConnectContext> options;

    [SetUp]
    public async Task SetUp()
    {
        databasePath = Path.Combine(Path.GetTempPath(), $"sky-mc-connect-{Guid.NewGuid():N}.db");
        options = new DbContextOptionsBuilder<ConnectContext>()
            .UseSqlite($"Data Source={databasePath};Default Timeout=30")
            .Options;
        await using var db = new ConnectContext(options);
        await db.Database.EnsureCreatedAsync();
    }

    [TearDown]
    public void TearDown()
    {
        File.Delete(databasePath);
    }

    [Test]
    public async Task ConcurrentIdenticalReservationsAreIdempotent()
    {
        var reservations = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            Reserve(new Challenge { AuctionUuid = "auction", UserId = "user", MinecraftUuid = "minecraft" })));

        Assert.That(reservations.Count(r => r == ChallengeReservationStatus.Created), Is.EqualTo(1));
        Assert.That(reservations.Count(r => r == ChallengeReservationStatus.Replay), Is.EqualTo(7));
    }

    [Test]
    public async Task ReplayingTheSameOwnerAndPayloadSucceeds()
    {
        var first = await Reserve(new Challenge { AuctionUuid = "auction", UserId = "user", MinecraftUuid = "minecraft" });
        var replay = await Reserve(new Challenge { AuctionUuid = "auction", UserId = "user", MinecraftUuid = "minecraft" });

        Assert.That(first, Is.EqualTo(ChallengeReservationStatus.Created));
        Assert.That(replay, Is.EqualTo(ChallengeReservationStatus.Replay));
    }

    [TestCase("other-user", "minecraft")]
    [TestCase("user", "other-minecraft")]
    public async Task ReusingAnAuctionForDifferentPayloadConflicts(string userId, string minecraftUuid)
    {
        await Reserve(new Challenge { AuctionUuid = "auction", UserId = "user", MinecraftUuid = "minecraft" });

        var result = await Reserve(new Challenge { AuctionUuid = "auction", UserId = userId, MinecraftUuid = minecraftUuid });

        Assert.That(result, Is.EqualTo(ChallengeReservationStatus.Conflict));
    }

    private async Task<ChallengeReservationStatus> Reserve(Challenge challenge)
    {
        await using var db = new ConnectContext(options);
        return (await ConnectService.ReserveChallenge(db, challenge)).Status;
    }
}
