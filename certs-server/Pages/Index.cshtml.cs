using CertsServer.Acme;
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
    private readonly ICertificateStore _store;

    public IndexModel(CertsServerDbContext db, IPublisher publisher, ICertificateStore store)
    {
        _db = db;
        _publisher = publisher;
        _store = store;
    }

    public List<TicketDto> Tickets { get; set; } = [];

    public async Task OnGetAsync()
    {
        var tickets = await _db.Tickets.AsNoTracking().ToListAsync(this.HttpContext.RequestAborted);
        Tickets = tickets
            .OrderByDescending(x => x.CreatedTime)
            .Select(
                t => new TicketDto(
                    t.Id,
                    t.CreatedTime,
                    t.Status,
                    t.Remark,
                    t.DomainNames))
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

    public async Task<IActionResult> OnPostDownloadAsync(Guid id)
    {
        var ticket = await _db.Tickets.FindAsync([id], this.HttpContext.RequestAborted);
        if (ticket is null)
        {
            return NotFound();
        }

        var latestOrder = await _db.TicketOrders.Where(x => x.TicketId == id && x.Deleted == false)
        .OrderByDescending(x => x.CreatedTime)
        .FirstOrDefaultAsync(this.HttpContext.RequestAborted);

        if (latestOrder?.Certificate is null)
        {
            return NotFound();
        }

        return RedirectToPage("/Tickets/Order", new { id = latestOrder.Id });
    }
}