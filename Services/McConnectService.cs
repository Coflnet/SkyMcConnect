using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Kafka;
using Coflnet.Sky.McConnect.Models;
using hypixel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTracing;

namespace Coflnet.Sky.McConnect
{
    public class McConnectService : BackgroundService
    {
        private IConfiguration configuration;
        private IServiceScopeFactory scopeFactory;
        private ConnectService connectSercie;

        public McConnectService(IConfiguration config,
                    IServiceScopeFactory scopeFactory,
                    ConnectService connectSercie)
        {
            configuration = config;
            this.scopeFactory = scopeFactory;
            this.connectSercie = connectSercie;
        }

        private Task ListenForValidations(CancellationToken cancleToken)
        {
            var kafkaHost = configuration["KAFKA_HOST"];
            var newAuctionTopic = configuration["TOPICS:NEW_AUCTION"];
            var newBidTopic = configuration["TOPICS:NEW_BID"];

            var newAuction = KafkaConsumer.Consume<hypixel.SaveAuction>(kafkaHost, newAuctionTopic, NewAuction, cancleToken, "mc-connect");

            var newBid = KafkaConsumer.Consume<hypixel.SaveAuction>(kafkaHost, newBidTopic, NewBid, cancleToken, "mc-connect");
            Console.WriteLine("started consuming");
            Console.WriteLine($"There are {connectSercie.ToConnect.Count} waiting for validation");
            return Task.WhenAll(new Task[] { newAuction, newBid, ClearOldFromLookup(cancleToken) });

        }

        private async Task ClearOldFromLookup(CancellationToken cancelToken)
        {
            var maxValidationAge = TimeSpan.FromMinutes(10);
            while (!cancelToken.IsCancellationRequested)
            {
                await Task.Delay(maxValidationAge / 2, cancelToken);
                var toRemove = new List<string>();
                var minTime = DateTime.Now.Subtract(maxValidationAge);
                foreach (var item in connectSercie.ToConnect)
                {
                    if (item.Value.LastRequestedAt < minTime)
                        toRemove.Add(item.Key);
                }
                foreach (var item in toRemove)
                {
                    Console.WriteLine("removing player " + item);
                    connectSercie.ToConnect.TryRemove(item, out MinecraftUuid uuid);
                }
                Console.WriteLine($"There are {connectSercie.ToConnect.Count} waiting for validation");
            }
        }

        private async Task NewAuction(SaveAuction auction)
        {
            if (auction.UId % 1000 == 0)
                Console.WriteLine("1000 auctions step " + DateTime.Now);
            if (!connectSercie.ToConnect.TryGetValue(auction.AuctioneerId, out MinecraftUuid minecraftUuid))
                return;
            using var factoryScope = scopeFactory.CreateScope();
            var tracer = factoryScope.ServiceProvider.GetRequiredService<ITracer>();
            using var scope = tracer.BuildSpan("AuctionValidation").WithTag("auctionId", auction.Uuid).WithTag("mcId", minecraftUuid.AccountUuid).StartActive();
            var uuid = auction.AuctioneerId;
            await ValidateAmount(auction.StartingBid, uuid, minecraftUuid.Id);
        }

        private async Task ValidateAmount(long amount, string uuid, int userId)
        {
            Console.Write("validating amount for " + uuid);
            if (!IsCorrectAmount(uuid, amount, userId))
                return;
            Console.WriteLine($"correct amount user {userId}");
            using (var scope = scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ConnectContext>();

                var minecraftUuid = (await db.Users.Where(u=>u.Id == userId).Include(u=>u.Accounts).FirstAsync())
                    .Accounts.Where(a=>a.AccountUuid == uuid).First();
                minecraftUuid.Verified = true;
                minecraftUuid.UpdatedAt = DateTime.Now;
                db.McIds.Update(minecraftUuid);
                await db.SaveChangesAsync();
            }
        }

        private bool IsCorrectAmount(string uuid, long amount, int userId)
        {
            var time = DateTime.Now;
            var secondTime = time.Subtract(TimeSpan.FromMinutes(5));
            var targetAmount = connectSercie.GetAmount(uuid, time, userId);
            Console.WriteLine($"Should be {targetAmount} is {amount}");
            return amount == targetAmount || amount == connectSercie.GetAmount(uuid, secondTime, userId);
        }


        private async Task NewBid(SaveAuction auction)
        {
            foreach (var bid in auction.Bids)
            {
                if (!connectSercie.ToConnect.TryGetValue(bid.Bidder, out MinecraftUuid minecraftUuid))
                    continue;
                Console.WriteLine("vaidating a bid " + auction.Uuid);
                using var factoryScope = scopeFactory.CreateScope();
                var tracer = factoryScope.ServiceProvider.GetRequiredService<ITracer>();
                using var scope = tracer.BuildSpan("BidValidation").WithTag("auctionId", auction.Uuid).WithTag("mcId", minecraftUuid.AccountUuid).StartActive();
                await ValidateAmount(bid.Amount, bid.Bidder, minecraftUuid.Id);
            }
            if (auction.UId % 500 == 0)
                Console.WriteLine("500 bids step");
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await connectSercie.Setup();
            await ListenForValidations(stoppingToken);
        }
    }

    public class ConnectionRequest
    {
        public int Code { get; set; }
        public bool IsConnected { get; set; }
    }
}
