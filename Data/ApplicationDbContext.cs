using Microsoft.EntityFrameworkCore;
using PicklePlay.Models;

namespace PicklePlay.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) 
            : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<Wallet> Wallets { get; set; }
        public DbSet<Escrow> Escrows { get; set; }
        public DbSet<Transaction> Transactions { get; set; }
        public DbSet<EscrowDispute> EscrowDisputes { get; set; }
        // Add other DbSets for your 25+ tables later
    }
}