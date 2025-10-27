using Microsoft.EntityFrameworkCore;
using PicklePlay.Models;

namespace PicklePlay.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) 
            : base(options) { }

        public DbSet<User> Users { get; set; }
        // Add other DbSets for your 25+ tables later
    }
}