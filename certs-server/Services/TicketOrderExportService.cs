using System.Security.Cryptography.X509Certificates;

using CertsServer.Acme;
using CertsServer.Data;

using PKISharp.SimplePKI;

namespace CertsServer.Services;

public class TicketOrderExportService
{
    private readonly CertsServerDbContext _db;
    private readonly IAcmeStore _store;
    private readonly AcmeClientFactory _clientFactory;

    public TicketOrderExportService(
        CertsServerDbContext db,
        IAcmeStore store,
        AcmeClientFactory clientFactory)
    {
        _db = db;
        _store = store;
        _clientFactory = clientFactory;
    }

    public async Task<byte[]?> GetPem(Guid ticketId, long orderId, CancellationToken cancellationToken)
    {
        var ticket = await _db.Tickets.FindAsync([ticketId], cancellationToken);
        var ticketOrder = await _db.TicketOrders.FindAsync([orderId], cancellationToken);

        if(ticket is null || ticketOrder is null || ticketOrder.Certificate is null)
        {
            return null;
        }

        return await GetPem(ticket, ticketOrder, cancellationToken);
    }

    private async Task<byte[]> GetPem(TicketEntity ticket, TicketOrderEntity ticketOrder, CancellationToken cancellationToken)
    {
        var fileBytes = await _store.LoadRawAsync<byte[]>(AcmeStoreKeys.AcmeOrderCert, cancellationToken, ticketOrder.Id);
        if (fileBytes is null)
        {
            using var acme = await _clientFactory.Create(cancellationToken: cancellationToken);
            var order = await acme.GetOrderDetailsAsync(ticketOrder.OrderUrl, default, cancellationToken);
            fileBytes = await acme.GetOrderCertificateAsync(order, cancellationToken);
            await _store.SaveRawAsync(fileBytes, AcmeStoreKeys.AcmeOrderCert, cancellationToken, ticketOrder.Id);
        }

        return fileBytes;
    }

    public async Task<byte[]?> GetPfx(Guid ticketId, long orderId, CancellationToken cancellationToken)
    {
        var ticket = await _db.Tickets.FindAsync([ticketId], cancellationToken);
        var ticketOrder = await _db.TicketOrders.FindAsync([orderId], cancellationToken);

        if (ticket is null || ticketOrder is null || ticketOrder.Certificate is null)
        {
            return null;
        }

        var pfxBytes = await _store.LoadRawAsync<byte[]>(AcmeStoreKeys.PfxFile, cancellationToken, ticketOrder.Id);
        
        if(pfxBytes is null)
        {
            var certKey = await _store.LoadRawAsync<string>(AcmeStoreKeys.AcmeOrderCertKey, cancellationToken, ticketOrder.Id);
            if(certKey is null)
            {
                throw new InvalidOperationException("Cannnot export PFX");
            }
            
            var keyPair = AcmeCertificateFactory.Base64ToPkiKeyPair(certKey);
            var pemBytes = await GetPem(ticket, ticketOrder, cancellationToken);
            using var cert = X509CertificateLoader.LoadCertificate(pemBytes);
            var pkiCert = PkiCertificate.From(cert);
            var pfx = pkiCert.Export(PkiArchiveFormat.Pkcs12,
                privateKey: keyPair.PrivateKey,
                password: string.IsNullOrWhiteSpace(ticket?.PfxPassword) ? null : ticket.PfxPassword.ToCharArray());

            return pfx;
        }

        return pfxBytes;
    }
}
