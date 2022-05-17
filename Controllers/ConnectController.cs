using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.McConnect.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.McConnect.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ConnectController : ControllerBase
    {
        private readonly ILogger<ConnectController> _logger;
        private readonly ConnectContext db;
        private readonly ConnectService connectService;

        public ConnectController(ILogger<ConnectController> logger, ConnectContext context, ConnectService connectService)
        {
            _logger = logger;
            db = context;
            this.connectService = connectService;
        }

        [HttpPost]
        [Route("user/{userId}")]
        public async Task<ConnectionRequest> CreateConnection(string userId, string mcUuid)
        {
            var user = await GetOrCreateUser(userId);
            return await connectService.AddNewRequest(user, mcUuid);
        }


        [HttpGet]
        [Route("user/{userId}")]
        public Task<User> GetConnections(string userId)
        {
            return GetOrCreateUser(userId, true);
        }

        /// <summary>
        /// Get all users stored which may or may not have a connected account
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("users")]
        public async Task<IEnumerable<User>> GetUsers(int amount = 1000, int offset = 0)
        {
            return await db.Users.Skip(offset).Take(amount).ToListAsync();
        }
        /// <summary>
        /// Get all users stored which may or may not have a connected account
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("users/connected")]
        public async Task<IEnumerable<User>> GetVerifiedUsers(int amount = 1000, int offset = 0)
        {
            return await db.Users.Where(u=>u.Accounts.Where(a=>a.Verified).Any()).Include(u => u.Accounts.Where(a=>a.Verified)).Skip(offset).Take(amount).ToListAsync();
        }

        private async Task<User> GetOrCreateUser(string userId, bool blockSave = false)
        {
            var user = await db.Users.Where(u => u.ExternalId == userId).Include(u => u.Accounts).FirstOrDefaultAsync();
            if (user == null)
            {
                user = new User() { ExternalId = userId };
                if (blockSave)
                    return user;
                db.Users.Add(user);
                await db.SaveChangesAsync();
            }

            return user;
        }

        [HttpGet]
        [Route("minecraft/{mcUuid}")]
        public async Task<User> GetUser(string mcUuid)
        {
            return await db.McIds.Where(id => id.AccountUuid == mcUuid && id.Verified).OrderByDescending(m => m.UpdatedAt).Select(id => id.User).FirstOrDefaultAsync();
        }
        [HttpPost]
        [Route("user/{userId}/verify")]
        public async Task GetUser(string userId, string mcUuid)
        {
            var user = await GetOrCreateUser(userId);
            var req = await connectService.AddNewRequest(user, mcUuid);
            var con = await db.McIds.Where(i => i.User == user && i.AccountUuid == mcUuid).FirstAsync();
            await connectService.ValidatedLink(con.Id);
        }
    }
}
