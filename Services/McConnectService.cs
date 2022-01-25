using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Kafka;
using Coflnet.Sky.McConnect.Models;
using Confluent.Kafka;
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
        private ProducerConfig producerConfig;

        public McConnectService(IConfiguration config,
                    IServiceScopeFactory scopeFactory,
                    ConnectService connectSercie)
        {
            configuration = config;
            this.scopeFactory = scopeFactory;
            this.connectSercie = connectSercie;

            producerConfig = new ProducerConfig
            {
                BootstrapServers = configuration["KAFKA_HOST"],
                LingerMs = 2
            };
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

        private async Task ValidateAmount(long amount, string uuid, int linkId)
        {
            Console.Write("validating amount for " + uuid);
            if (!IsCorrectAmount(uuid, amount, linkId))
                return;
            Console.WriteLine($"correct amount user {linkId}");
            using (var scope = scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ConnectContext>();

                var minecraftUuid = await db.McIds.Where(id => id.Id == linkId).Include(id=>id.User).FirstAsync();
                minecraftUuid.Verified = true;
                minecraftUuid.UpdatedAt = DateTime.Now;
                var eventTask = ProduceEvent(new VerificationEvent()
                {
                    MinecraftUuid = minecraftUuid.AccountUuid,
                    UserId = minecraftUuid.User.ExternalId
                });
                db.McIds.Update(minecraftUuid);
                await db.SaveChangesAsync();
                await eventTask;
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

        public async Task ProduceEvent(VerificationEvent transactionEvent)
        {
            using (var p = new ProducerBuilder<Null, VerificationEvent>(producerConfig).SetValueSerializer(SerializerFactory.GetSerializer<VerificationEvent>()).Build())
            {
                await p.ProduceAsync(configuration["TOICS:VERIFIED"], new Message<Null, VerificationEvent>()
                {
                    Value = transactionEvent
                });
            }
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await connectSercie.Setup();
            await ListenForValidations(stoppingToken);
        }
    }

}
