using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Coflnet.Sky.McConnect.Models;
using Coflnet.Sky.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Coflnet.Kafka;

namespace Coflnet.Sky.McConnect
{
    /// <summary>
    /// Client service for mc connect aka acoount verify 
    /// </summary>
    public class ConnectService
    {
        private IServiceScopeFactory scopeFactory;
        private KafkaCreator kafkaCreator;
        private IConfiguration config;
        private readonly string secret;
        public ConcurrentDictionary<string, MinecraftUuid> ToConnect = new ConcurrentDictionary<string, MinecraftUuid>();
        public ConcurrentDictionary<string, Challenge> Challenges = new();
        private ILogger<ConnectService> logger;
        private ProducerConfig producerConfig;

        private static Prometheus.Counter conAttempts = Prometheus.Metrics.CreateCounter("sky_mccon_attempts", "How many connection attempts were made within 10");


        /// <summary>
        /// Creates a new Instance of the <see cref="ConnectService"/>
        /// </summary>
        /// <param name="config"></param>
        /// <param name="scopeFactory"></param>
        /// <param name="logger"></param>
        /// <param name="kafkaCreator"></param>
        public ConnectService(
            IConfiguration config,
                    IServiceScopeFactory scopeFactory,
                    ILogger<ConnectService> logger,
                    KafkaCreator kafkaCreator)
        {
            this.scopeFactory = scopeFactory;
            this.config = config;
            this.secret = config["TOKEN_SECRET"];
            Console.WriteLine("created new");
            this.logger = logger;


            producerConfig = new ProducerConfig
            {
                BootstrapServers = config["KAFKA_HOST"],
                LingerMs = 2
            };
            this.kafkaCreator = kafkaCreator;
        }

        /// <summary>
        /// Add a new <see cref="ConnectionRequest"/> to the service and db 
        /// </summary>
        /// <param name="user">The user to add the id to</param>
        /// <param name="minecraftUuid">The uuid to add</param>
        /// <returns></returns>
        public async Task<ConnectionRequest> AddNewRequest(Models.User user, string minecraftUuid)
        {
            conAttempts.Inc();
            var response = new ConnectionRequest();
            var accountInstance = user?.Accounts?.Where(a => a.AccountUuid == minecraftUuid).FirstOrDefault();
            // TODO: remove this if when users are allowed to have and can switch between  multiple accounts
            //if (user?.Accounts?.OrderByDescending(a => a.UpdatedAt).Where(a => a.Verified).FirstOrDefault() == accountInstance)
            response.IsConnected = accountInstance?.Verified ?? false;

            if (minecraftUuid == null)
                throw new CoflnetException("uuid_is_null", "minecraftUuid is null");

            using (var scope = scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ConnectContext>();
                if (accountInstance != null)
                {
                    accountInstance.LastRequestedAt = DateTime.UtcNow;
                    db.McIds.Update(accountInstance);
                    await db.SaveChangesAsync();
                    ToConnect[minecraftUuid] = accountInstance;
                    response.Code = GetAmount(minecraftUuid, DateTime.UtcNow, accountInstance.Id);
                    return response;
                }

                var newId = new MinecraftUuid() { AccountUuid = minecraftUuid };
                user.Accounts.Add(newId);
                db.Update(user);
                await db.SaveChangesAsync();
                ToConnect[minecraftUuid] = newId;
                response.Code = GetAmount(minecraftUuid, DateTime.UtcNow, newId.Id);
                return response;
            }
        }

        public async Task AddChallenge(Challenge challenge)
        {
            using (var scope = scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ConnectContext>();
                challenge.CreatedAt = DateTime.UtcNow;
                await db.Challenges.AddAsync(challenge);
                await db.SaveChangesAsync();
            }
            Challenges[challenge.AuctionUuid] = challenge;
        }

        public async Task Setup()
        {
            Console.WriteLine("setting up");
            using (var scope = scopeFactory.CreateScope())
            {
                try
                {
                    var minTime = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(15));
                    var db = scope.ServiceProvider.GetRequiredService<ConnectContext>();
                    await db.Database.MigrateAsync();
                    Console.WriteLine("migrated");
                    ToConnect = new ConcurrentDictionary<string, MinecraftUuid>(await db.McIds
                        .Where(id => !id.Verified)
                        .Where(id => id.LastRequestedAt > minTime)
                        .ToDictionaryAsync(a => a.AccountUuid));
                    Challenges = new ConcurrentDictionary<string, Challenge>(await db.Challenges
                        .Where(c => c.CreatedAt > minTime)
                        .ToDictionaryAsync(c => c.AuctionUuid));
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                    Console.WriteLine(e.StackTrace);
                }
            }
            await kafkaCreator.CreateTopicIfNotExist(config["TOPICS:VERIFIED"]);
            Console.WriteLine("done with setup");
        }

        public int GetAmount(string uuid, DateTime timeStamp, int conId)
        {
            var bytes = Encoding.UTF8.GetBytes(uuid.ToLower() + conId + timeStamp.RoundDown(TimeSpan.FromMinutes(10)).ToString() + secret);
            var hash = System.Security.Cryptography.SHA512.Create();
            return Math.Abs(BitConverter.ToInt32(hash.ComputeHash(bytes))) % 980 + 14;
        }

        public async Task ValidatedLink(int linkId)
        {
            using (var scope = scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ConnectContext>();

                var minecraftUuid = await db.McIds.Where(id => id.Id == linkId).Include(id => id.User).FirstAsync();
                var existingCount = await db.McIds.Where(id => id.AccountUuid == minecraftUuid.AccountUuid && id.Verified).CountAsync();
                logger.LogInformation($"{minecraftUuid.AccountUuid} has {existingCount} verified accounts");
                minecraftUuid.Verified = true;
                minecraftUuid.UpdatedAt = DateTime.UtcNow;
                db.McIds.Update(minecraftUuid);
                await db.SaveChangesAsync();

                var eventTask = ProduceEvent(new VerificationEvent()
                {
                    MinecraftUuid = minecraftUuid.AccountUuid,
                    UserId = minecraftUuid.User.ExternalId,
                    ExistingConCount = existingCount
                });
                await eventTask;
            }
        }

        public async Task ProduceEvent(VerificationEvent transactionEvent)
        {
            try
            {
                using (var p = kafkaCreator.BuildProducer<string, VerificationEvent>())
                {
                    await p.ProduceAsync(config["TOPICS:VERIFIED"], new Message<string, VerificationEvent>()
                    {
                        Value = transactionEvent,
                        Key = $"{transactionEvent.MinecraftUuid}-{transactionEvent.UserId}"
                    });
                }
                logger.LogInformation($"Produced verification event for {transactionEvent.MinecraftUuid} ({transactionEvent.UserId}) with {transactionEvent.ExistingConCount} existing connections");
            }
            catch (Exception e)
            {
                logger.LogError(e, "Trying to produce verification event");
            }
        }

        internal async Task Sold(SaveAuction item)
        {
            if (!Challenges.TryGetValue(item.Uuid, out Challenge challenge))
                return;
            var highestBidder = item.Bids.OrderByDescending(b => b.Amount).First();
            if (challenge.CreatedAt < highestBidder.Timestamp - TimeSpan.FromMinutes(1))
                return;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ConnectContext>();
            // challenge completed
            challenge.BoughtBy = highestBidder.Bidder;
            challenge.BoughtAt = highestBidder.Timestamp;
            challenge.CompletedAt = DateTime.UtcNow;
            db.Challenges.Update(challenge);
            await db.SaveChangesAsync();
            // check if enough challenges completed
            var minTime = DateTime.UtcNow.Subtract(TimeSpan.FromDays(1));
            var challenges = await db.Challenges.Where(c => c.CompletedAt > minTime).ToListAsync();
            var matching = challenges.Where(c => c.UserId == challenge.UserId && c.BoughtBy == challenge.BoughtBy).ToList();
            if (matching.Count < 3)
            {
                logger.LogInformation($"Challenge incomplete for {challenge.BoughtBy} ({challenge.UserId}) connected as {challenge.MinecraftUuid} at {challenge.BoughtAt} ");
                return;
            }
            logger.LogInformation($"Challenge completed for {challenge.BoughtBy} ({challenge.UserId}) connected as {challenge.MinecraftUuid} at {challenge.BoughtAt}");
        }
    }
}
