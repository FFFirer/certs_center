using CertsServer.Events;

using MediatR;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

using Tea.Utils;

namespace CertsServer.Pages;

public class IndexModel : PageModel
{
    private readonly CertsServerDbContext _db;
    private readonly IPublisher _publisher;

    public IndexModel(CertsServerDbContext db, IPublisher publisher)
    {
        _db = db;
        _publisher = publisher;
    }

    public List<TicketDto> Tickets { get; set; } = [];

    public async Task OnGetAsync()
    {
        var tickets = await _db.Tickets.AsNoTracking().Include(x => x.Certificates).ToListAsync(this.HttpContext.RequestAborted);
        Tickets = tickets
            .OrderByDescending(x => x.CreatedTime)
            .Select(
                t => new TicketDto(
                    t.Id,
                    t.CreatedTime,
                    t.Status,
                    t.Remark,
                    t.DomainNames,
                    t.Certificates?.Select(
                        x => new CertificateDto(
                            x.Id,
                            x.Path,
                            x.Status,
                            x.NotBefore,
                            x.NotAfter,
                            x.CreatedTime))?.ToArray() ?? []))
            .ToList();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var ticket = await _db.Tickets.FindAsync(id);

        if (ticket != null)
        {
            ticket.Status = Data.TicketStatus.Deleted;
            await _db.SaveChangesAsync(this.HttpContext.RequestAborted);

            await _publisher.Publish(new TicketDeleted(ticket.Id));
        }

        return RedirectToPage();
    }
}