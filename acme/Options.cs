namespace CertsServer.Acme;

public class AcmeOptions
{
    public string[] Email { get; set; } = [];
    public bool AcceptTermOfService { get; set; }
    public string CaEndpoint { get; set; } = "";
    public string CaName { get; set; } = "";
}
