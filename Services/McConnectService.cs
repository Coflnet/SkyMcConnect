using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Kafka;
using Coflnet.Sky.McConnect.Models;
using Confluent.Kafka;
using Coflnet.Sky.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Coflnet.Sky.McConnect
{
    public class McConnectService : BackgroundService
    {
        private IConfiguration configuration;
        private IServiceScopeFactory scopeFactory;
        private ConnectService connectSercie;
        private ILogger<McConnectService> logger;

        public McConnectService(IConfiguration config,
                    IServiceScopeFactory scopeFactory,
                    ConnectService connectSercie, ILogger<McConnectService> logger)
        {
            configuration = config;
            this.scopeFactory = scopeFactory;
            this.connectSercie = connectSercie;

            this.logger = logger;
        }

        private async Task ListenForValidations(CancellationToken cancleToken)
        {
            var kafkaHost = configuration["KAFKA_HOST"];
            var newAuctionTopic = configuration["TOPICS:NEW_AUCTION"];
            var newBidTopic = configuration["TOPICS:NEW_BID"];
            var consumerGroup = "mc-connect" + System.Net.Dns.GetHostName().Last();

            var newAuction = KafkaConsumer.ConsumeBatch<SaveAuction>(configuration, newAuctionTopic, async auctions =>
            {
                foreach (var item in auctions)
                {
                    await NewAuction(item);
                }
            }, cancleToken, consumerGroup);

            var newBid = KafkaConsumer.ConsumeBatch<SaveAuction>(configuration, newBidTopic, async bids =>
            {
                foreach (var item in bids)
                {
                    await NewBid(item);
                }
            }, cancleToken, consumerGroup);
            Console.WriteLine("started consuming");
            Console.WriteLine($"There are {connectSercie.ToConnect.Count} waiting for validation");
            await Task.WhenAny(new Task[] { Wrap(newAuction,"auctions"), Wrap(newBid, "bids"), Wrap(ClearOldFromLookup(cancleToken), "clear") });
            throw new Exception("either bids or auctions stopped consuming");

        }

        private async Task Wrap(Task toWarp, string message)
        {
            try
            {
                await toWarp;
            }
            catch (System.Exception e)
            {
                logger.LogError(e, message);
                throw;
            }
        }

        private async Task ClearOldFromLookup(CancellationToken cancelToken)
        {
            var maxValidationAge = TimeSpan.FromMinutes(10);
            while (!cancelToken.IsCancellationRequested)
            {
                await Task.Delay(maxValidationAge / 2, cancelToken);
                var toRemove = new List<string>();
                var minTime = DateTime.UtcNow.Subtract(maxValidationAge);
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
                Console.WriteLine("1000 auctions step " + DateTime.UtcNow);
            if (!connectSercie.ToConnect.TryGetValue(auction.AuctioneerId, out MinecraftUuid minecraftUuid))
                return;
            using var factoryScope = scopeFactory.CreateScope();
            var tracer = factoryScope.ServiceProvider.GetRequiredService<ActivitySource>();
            using var scope = tracer.StartActivity("AuctionValidation").AddTag("auctionId", auction.Uuid).AddTag("mcId", minecraftUuid.AccountUuid);
            var uuid = auction.AuctioneerId;
            await ValidateAmount(auction.StartingBid, uuid, minecraftUuid.Id);
        }

        private async Task ValidateAmount(long amount, string uuid, int linkId)
        {
            Console.Write("validating amount for " + uuid);
            amount = amount % 1000;
            if (!IsCorrectAmount(uuid, amount, linkId))
                return;
            Console.WriteLine($"correct amount user {linkId}");
            await connectSercie.ValidatedLink(linkId);
        }

        

        public async Task ForceVerify(long amount, string uuid, int linkId)
        {
            await ValidateAmount(amount, uuid, linkId);
        }

        private bool IsCorrectAmount(string uuid, long amount, int userId)
        {
            var time = DateTime.UtcNow;
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
                var tracer = factoryScope.ServiceProvider.GetRequiredService<ActivitySource>();
                using var scope = tracer.StartActivity("BidValidation").AddTag("auctionId", auction.Uuid).AddTag("mcId", minecraftUuid.AccountUuid);
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

}
