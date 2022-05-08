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
            return await connectService.AddNewRequest(user,mcUuid);
        }


        [HttpGet]
        [Route("user/{userId}")]
        public Task<User> GetConnections(string userId)
        {
            return GetOrCreateUser(userId);
        }

        private async Task<User> GetOrCreateUser(string userId)
        {
            var user = await db.Users.Where(u => u.ExternalId == userId).Include(u => u.Accounts).FirstOrDefaultAsync();
            if (user == null)
            {
                user = new User() { ExternalId = userId };
                db.Users.Add(user);
                await db.SaveChangesAsync();
            }

            return user;
        }

        [HttpGet]
        [Route("minecraft/{mcUuid}")]
        public async Task<User> GetUser(string mcUuid)
        {
            return await db.McIds.Where(id=>id.AccountUuid == mcUuid && id.Verified).OrderByDescending(m=>m.UpdatedAt).Select(id=>id.User).FirstOrDefaultAsync();
        }
        [HttpPost]
        [Route("user/{userId}/verify")]
        public async Task GetUser(string userId, string mcUuid)
        {
            var user = await GetOrCreateUser(userId);
            var req = await connectService.AddNewRequest(user,mcUuid);
            var con = await db.McIds.Where(i=>i.User == user && i.AccountUuid == mcUuid).FirstAsync();
            await connectService.ValidatedLink(con.Id);
        }
    }
}
