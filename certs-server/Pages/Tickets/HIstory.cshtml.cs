using System.Runtime.ConstrainedExecution;

using CertsServer.Acme;
using CertsServer.Data;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CertsServer.Pages.Tickets;

public class HistoryModel : PageModel
{
    private readonly CertsServerDbContext _db;
    private readonly ICertificateStore _store;
    private readonly AcmeCertificateFactory _certificateFactory;

    public HistoryModel(CertsServerDbContext db, ICertificateStore store, AcmeCertificateFactory certificateFactory)
    {
        _db = db;
        _store = store;
        _certificateFactory = certificateFactory;
    }

    public List<TicketOrderEntity> Orders { get; set; } = [];

    public async Task OnGetAsync([FromRoute] Guid id)
    {
        this.Orders = await _db.TicketOrders.Where(x => x.TicketId == id).ToListAsync(this.HttpContext.RequestAborted);
    }
}