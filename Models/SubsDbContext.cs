using Microsoft.EntityFrameworkCore;

namespace Coflnet.Sky.McConnect.Models
{
    /// <summary>
    /// <see cref="DbContext"/> saving MC-Uuids
    /// </summary>
    public class ConnectContext : DbContext
    {
        /// <summary>
        /// Connected Users
        /// </summary>
        /// <value></value>
        public DbSet<User> Users { get; set; }

        public DbSet<MinecraftUuid> McIds { get; set; }
        public DbSet<Challenge> Challenges { get; set; }

        /// <summary>
        /// Creates a new instance of <see cref="ConnectContext"/>
        /// </summary>
        /// <param name="options"></param>
        public ConnectContext(DbContextOptions<ConnectContext> options)
        : base(options)
        {
        }

        public ConnectContext()
        {
        }

        /// <summary>
        /// Configures additional relations and indexes
        /// </summary>
        /// <param name="modelBuilder"></param>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>(entity =>
            {
                entity.HasIndex(e => e.ExternalId).IsUnique();
            });

            modelBuilder.Entity<MinecraftUuid>(entity =>
            {
                entity.HasIndex(e => e.AccountUuid);
            });
            modelBuilder.Entity<Challenge>(entity =>
            {
                entity.HasIndex(e => e.AuctionUuid).IsUnique();
                entity.HasIndex(e => e.UserId);
            });
        }
    }
}