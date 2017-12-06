using gc2lti_shared.Models;
using Microsoft.EntityFrameworkCore;

namespace gc2lti_shared.Data
{
    public class Gc2LtiDbContext : DbContext
    {
        public Gc2LtiDbContext (DbContextOptions options)
            : base(options)
        {
        }

        public DbSet<GoogleUser> GoogleUsers { get; set; }
        public DbSet<Item> Items { get; set; }
    }
}
