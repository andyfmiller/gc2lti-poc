using Microsoft.EntityFrameworkCore;
using GoogleUser = gc2lti_outcomes.Models.GoogleUser;
using Item = gc2lti_outcomes.Models.Item;

namespace gc2lti_outcomes.Data
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
