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

        public ConnectService(
            IConfiguration config,
                    IServiceScopeFactory scopeFactory)
        {
            this.scopeFactory = scopeFactory;
            this.config = config;
            this.secret = config["TOKEN_SECRET"];
            Console.WriteLine("created new");
        }

        public async Task<ConnectionRequest> AddNewRequest(Models.User user, string minecraftUuid)
        {
            var response = new ConnectionRequest();
            var accountInstance = user?.Accounts?.Where(a => a.AccountUuid == minecraftUuid).FirstOrDefault();
            response.IsConnected = accountInstance?.Verified ?? false;

            using (var scope = scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ConnectContext>();
                if (accountInstance != null)
                {
                    accountInstance.LastRequestedAt = DateTime.Now;
                    db.McIds.Update(accountInstance);
                    await db.SaveChangesAsync();
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
            using (var scope = scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ConnectContext>();
                await db.Database.MigrateAsync();
                ToConnect = new ConcurrentDictionary<string, MinecraftUuid>(await db.McIds
                    .Where(id => !id.Verified)
                    .Where(id => id.CreatedAt > DateTime.Now.Subtract(TimeSpan.FromMinutes(15)))
                    .ToDictionaryAsync(a => a.AccountUuid));
            }
        }

        public int GetAmount(string uuid, DateTime timeStamp, int conId)
        {
            var bytes = Encoding.UTF8.GetBytes(uuid.ToLower() + conId + timeStamp.RoundDown(TimeSpan.FromMinutes(10)).ToString() + secret);
            var hash = System.Security.Cryptography.SHA512.Create();
            return Math.Abs(BitConverter.ToInt32(hash.ComputeHash(bytes))) % 980 + 19;
        }
    }
}
