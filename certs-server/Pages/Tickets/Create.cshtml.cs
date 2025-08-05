using CertsServer.Data;
using CertsServer.Events;

using MediatR;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace CertsServer.Pages.Tickets;

public class CreateModel : PageModel
{
    public string? ErrorMessage { get; set; }

    [BindProperty]
    public string? PfxPassword { get; set; }
    [BindProperty]
    public string? DomainNames { get; set; }

    private readonly CertsServerDbContext _db;
    private readonly IPublisher _publisher;

    public CreateModel(CertsServerDbContext db, IPublisher publisher)
    {
        _db = db;
        _publisher = publisher;
    }

    public void OnGetAsync()
    {

    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(DomainNames))
        {
            ModelState.AddModelError(nameof(DomainNames), "Domain names is required");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var domains = DomainNames!.Split(',').Select(x => x.Trim());

        var ticket = new TicketEntity()
        {
            DomainNames = domains.ToArray(),
            PfxPassword = this.PfxPassword,
            Status = TicketStatus.Created
        };

        await _db.AddAsync(ticket, this.HttpContext.RequestAborted);
        await _db.SaveChangesAsync(this.HttpContext.RequestAborted);

        await _publisher.Publish(new TicketCreated(ticket.Id));

        return RedirectToPage("/Index");
    }
}
