using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Coflnet.Sky.McConnect.Models;
using hypixel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Coflnet.Sky.McConnect
{
    public class ConnectService
    {
        private IServiceScopeFactory scopeFactory;
        private IConfiguration config;
        private readonly string secret;
        public ConcurrentDictionary<string, MinecraftUuid> ToConnect = new ConcurrentDictionary<string, MinecraftUuid>();

        private static Prometheus.Counter conAttempts = Prometheus.Metrics.CreateCounter("sky_mccon_attempts", "How many connection attempts were made within 10");


        /// <summary>
        /// Creates a new Instance of the <see cref="ConnectService"/>
        /// </summary>
        /// <param name="config"></param>
        /// <param name="scopeFactory"></param>
        public ConnectService(
            IConfiguration config,
                    IServiceScopeFactory scopeFactory)
        {
            this.scopeFactory = scopeFactory;
            this.config = config;
            this.secret = config["TOKEN_SECRET"];
            Console.WriteLine("created new");
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
            if (user?.Accounts?.OrderByDescending(a => a.UpdatedAt).Where(a => a.Verified).FirstOrDefault() == accountInstance)
                response.IsConnected = accountInstance?.Verified ?? false;

            using (var scope = scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ConnectContext>();
                if (accountInstance != null)
                {
                    accountInstance.LastRequestedAt = DateTime.Now;
                    db.McIds.Update(accountInstance);
                    await db.SaveChangesAsync();
                    ToConnect[minecraftUuid] = accountInstance;
                    response.Code = GetAmount(minecraftUuid, DateTime.Now, accountInstance.Id);
                    return response;
                }

                var newId = new MinecraftUuid() { AccountUuid = minecraftUuid };
                user.Accounts.Add(newId);
                db.Update(user);
                await db.SaveChangesAsync();
                ToConnect[minecraftUuid] = newId;
                response.Code = GetAmount(minecraftUuid, DateTime.Now, newId.Id);
                return response;
            }
        }

        public async Task Setup()
        {
            Console.WriteLine("setting up");
            using (var scope = scopeFactory.CreateScope())
            {
                try
                {
                    var minTime = DateTime.Now.Subtract(TimeSpan.FromMinutes(15));
                    var db = scope.ServiceProvider.GetRequiredService<ConnectContext>();
                    await db.Database.MigrateAsync();
                    Console.WriteLine("migrated");
                    ToConnect = new ConcurrentDictionary<string, MinecraftUuid>(await db.McIds
                        .Where(id => !id.Verified)
                        .Where(id => id.LastRequestedAt > minTime)
                        .ToDictionaryAsync(a => a.AccountUuid));
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                    Console.WriteLine(e.StackTrace);
                }
            }
            Console.WriteLine("done with setup");
        }

        public int GetAmount(string uuid, DateTime timeStamp, int conId)
        {
            var bytes = Encoding.UTF8.GetBytes(uuid.ToLower() + conId + timeStamp.RoundDown(TimeSpan.FromMinutes(10)).ToString() + secret);
            var hash = System.Security.Cryptography.SHA512.Create();
            return Math.Abs(BitConverter.ToInt32(hash.ComputeHash(bytes))) % 980 + 19;
        }
    }
}
