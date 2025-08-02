using System;
using System.Threading.Tasks;

using CertsServer.Data;
using CertsServer.QuartzJobs;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Moq;

using Quartz;

using Serilog;

namespace CertsServer.Acme.Tests;

public class HandleTicketJobTest
{
    [Fact]
    public async Task Handle_Except_Success()
    {
        var CONST_TICKET_ID = new Guid("29d7f3dd-ffcb-40aa-beef-3a4b229865dd");

        var configuration = new ConfigurationBuilder()
            .AddUserSecrets("5be23d08-54c4-488c-acff-cca75c11ef03")
            .Build();

        var mockExecuationContext = new Mock<IJobExecutionContext>();
        mockExecuationContext.Setup(x => x.MergedJobDataMap)
            .Returns(() =>
            {
                return new JobDataMap((IDictionary<string, object>)new Dictionary<string, object>
                {
                    [HandleTicketJob.DataMapKeys.TicketId] = CONST_TICKET_ID
                });
            });

        var logger = new LoggerConfiguration()
            .WriteTo.Console()
            .Enrich.FromLogContext()
            .ReadFrom.Configuration(configuration)
            .CreateLogger();

        var services = new ServiceCollection();
        services.AddSerilog(logger: logger);
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDbContext<CertsServerDbContext>(options => options.UseInMemoryDatabase(nameof(Handle_Except_Success)));
        services.AddLogging(logging => logging.ClearProviders().AddConsole());
        services.AddScoped<HandleTicketJob>();
        SignFlowUnitTests.ConfigureAcmeServices(services, configuration);

        var serviceProvider = services.BuildServiceProvider();

        using (var prepareScope = serviceProvider.CreateAsyncScope())
        {
            var db = prepareScope.ServiceProvider.GetRequiredService<CertsServerDbContext>();
            db.Tickets.Add(new Data.TicketEntity()
            {
                Id = CONST_TICKET_ID,
                DomainNames = ["test1.fffirer.top", "test2.fffirer.top"],
                PfxPassword = "123456",
                Status = Data.TicketStatus.Created,
                CreatedTime = DateTimeOffset.UtcNow,
                UpdatedTime = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        // test 
        using (var execScope = serviceProvider.CreateAsyncScope())
        {
            var job = execScope.ServiceProvider.GetRequiredService<HandleTicketJob>();
            await job.Execute(mockExecuationContext.Object);
        }

        // assert
        using (var assertScope = serviceProvider.CreateAsyncScope())
        {
            var db = assertScope.ServiceProvider.GetRequiredService<CertsServerDbContext>();
            var ticket = await db.Tickets.Where(x => x.Id == CONST_TICKET_ID).Include(x => x.Certificates).FirstOrDefaultAsync();

            Assert.NotNull(ticket);
            Assert.Equal(Data.TicketStatus.Finished, ticket.Status);
            Assert.NotEmpty(ticket.Certificates);
            Assert.All(ticket.Certificates, a => Assert.Equal(TicketCertificateStatus.Active, a.Status));
        }
    }
}
