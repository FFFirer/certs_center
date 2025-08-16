using CertsServer.Data;
using CertsServer.Services;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CertsServer.Pages.Tickets;

public class TicketOrderModel : PageModel
{
    public TicketOrderEntity? TicketOrder;

    private readonly CertsServerDbContext _db;
    private readonly TicketOrderExportService _export;

    public TicketOrderModel(CertsServerDbContext db, TicketOrderExportService export)
    {
        _db = db;
        _export = export;
    }

    public async Task OnGetAsync(long id)
    {
        TicketOrder = await _db.TicketOrders.FindAsync([id], this.HttpContext.RequestAborted);
    }

    public async Task<IActionResult> OnPostDownloadAsync([FromRoute] long id, string type)
    {
        var ticketOrder = await _db.TicketOrders.FindAsync([id], this.HttpContext.RequestAborted);
        if (ticketOrder is null)
        {
            return NotFound();
        }

        var bytes = type switch
        {
            "pfx" => await _export.GetPfx(ticketOrder.TicketId, ticketOrder.Id, this.HttpContext.RequestAborted),
            "pem" => await _export.GetPem(ticketOrder.TicketId, ticketOrder.Id, this.HttpContext.RequestAborted),
            _ => throw new NotSupportedException($"Not supported export type: {type}")
        };

        if (bytes is null)
        {
            return NotFound();
        }

        var fileExt = type switch
        {
            "pfx" => ".pfx",
            "pem" => ".pem",
            _ => ""
        };

        return File(bytes, "application/octet-stream", $"{ticketOrder.Id}{fileExt}");
    }
}