using CertsServer.QuartzJobs;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

using Quartz;
using Quartz.Impl.Matchers;

namespace CertsServer.Pages.QuartzAdmin
{
    public class IndexModel : PageModel
    {
        private readonly ISchedulerFactory _schedulerFactory;

        public IndexModel(ISchedulerFactory schedulerFactory)
        {
            _schedulerFactory = schedulerFactory;
        }


        public async Task OnGetAsync()
        {
            var schd = await _schedulerFactory.GetScheduler(this.HttpContext.RequestAborted);
            var triggers = await schd.GetTriggersOfJob(HandleTicketJob.JobKey, this.HttpContext.RequestAborted);

            this.Triggers = triggers.Select(
                x => new TriggerDto(x.Key, x.JobKey, x.GetPreviousFireTimeUtc()?.LocalDateTime, x.GetNextFireTimeUtc()?.LocalDateTime)).ToList();
        }

        public async Task<IActionResult> OnPostExecuteNowAsync(Guid ticketId, string name, string group)
        {
            var jobkey = new JobKey(name, group);
            var schd = await _schedulerFactory.GetScheduler(this.HttpContext.RequestAborted);

            await schd.TriggerJob(
                jobkey,
                new JobDataMap
                {
                    { HandleTicketJob.DataMapKeys.TicketId, ticketId}
                },
                this.HttpContext.RequestAborted);

            return RedirectToPage();
        }

        public List<TriggerDto> Triggers { get; set; } = [];


        public record TriggerDto(TriggerKey Key, JobKey JobKey, DateTimeOffset? PreviousFireTime, DateTimeOffset? NextFireTime);
    }
}
