using System;
using ACMESharp;
using ACMESharp.Crypto.JOSE;

namespace CertsServer.Acme;

public class AccountKey(string keyType, string keyExport)
{
    public string KeyType { get; set; } = keyType;
    public string KeyExport { get; set; } = keyExport;

    public IJwsTool GenerateTool()
    {
        if (KeyType.StartsWith("ES"))
        {
            var tool = new ACMESharp.Crypto.JOSE.Impl.ESJwsTool();
            tool.HashSize = int.Parse(KeyType.Substring(2));
            tool.Init();
            tool.Import(KeyExport);
            return tool;
        }

        if (KeyType.StartsWith("RS"))
        {
            var tool = new ACMESharp.Crypto.JOSE.Impl.RSJwsTool();
            tool.HashSize = int.Parse(KeyType.Substring(2));
            tool.Init();
            tool.Import(KeyExport);
            return tool;
        }

        throw new Exception($"Unknown or unsupported KeyType [{KeyType}]");
    }
}

public class CertificateRequest(Guid id, string[] domains)
{
    public Guid Id { get; } = id;
    public string[] Domains { get; } = domains;

    public string? PfxPassword { get; set; }

    public int? KeySize { get; set; }
    public string KeyAlgor { get; set; } = AcmeConst.EcKeyType;
}