using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

using Tea.Utils;

namespace CertsServer.Pages;

public class IndexModel : PageModel
{
    private readonly CertsServerDbContext _db;

    public IndexModel(CertsServerDbContext db)
    {
        _db = db;
    }

    public List<TicketDto> Tickets { get; set; } = [];

    public async Task OnGetAsync()
    {
        var tickets = await _db.Tickets.AsNoTracking().Include(x => x.Certificates).ToListAsync(this.HttpContext.RequestAborted);
        Tickets = tickets
            .Select(
                t => new TicketDto(
                    t.Id,
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
}