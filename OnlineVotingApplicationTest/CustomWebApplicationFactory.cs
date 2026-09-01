using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestPlatform.TestHost;
using OnlineVotingApplication.Areas.Identity.Data;
using Microsoft.EntityFrameworkCore.InMemory;

namespace OnlineVotingApplicationTest
{
    public class CustomWebApplicationFactory : WebApplicationFactory<Program>
    {

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.UseSetting("ConnectionStrings:Default", "Host=localhost;Database=test;Username=test;Password=test");

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options =>
                    options.UseInMemoryDatabase($"OnlineVoting-tests-{Guid.NewGuid()}"));
            });
        }
    }
}

