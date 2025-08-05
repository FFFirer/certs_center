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
            var triggerKeys = await schd.GetTriggerKeys(GroupMatcher<TriggerKey>.AnyGroup(), this.HttpContext.RequestAborted);


            // this.Triggers = triggers.Select(t => new TriggerDto(t.Key.Name)).ToList();
            this.Triggers = triggerKeys.Select(x => new TriggerDto(x.Name)).ToList();
        }

        public List<TriggerDto> Triggers { get; set; } = [];


        public record TriggerDto(string? Name);
    }
}
