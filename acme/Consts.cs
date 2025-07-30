namespace CertsServer.Acme;


public record CaOptions(string Name, string Endpoint);

public static class CaConsts
{
    public static CaOptions LetsEncrypt = new CaOptions(nameof(LetsEncrypt), "https://acme-v02.api.letsencrypt.org/");
    public static CaOptions LetsEncryptStaging = new CaOptions(nameof(LetsEncryptStaging), "https://acme-staging-v02.api.letsencrypt.org/");
}


public class AcmeStoreKeys
{
    public const string AcmeAccountDetails = "01_Account";
    public const string AcmeAccountKey = "02_AccountKey";
    public const string AcmeDirectory = "03_Directory";
    public const string AcmeOrder = "04_Order_{0}";
    public const string AcmeOrderAuthz = "04_Order_{0}_Authz_{1}";
    public const string AcmeOrderAuthzChallenge = "04_Order_{0}_Authz_{1}_Challenge_{2}";
    public const string AcmeOrderCertKey = "04_Order_{0}_CertKey";
    public const string AcmeOrderCertCsr = "04_Order_{0}_CertCsr";
    public const string AcmeOrderCert = "04_Order_{0}_Cert";
    public const string PfxFile = "04_Order_{0}_Pfx";
    public const string DomainRecord = "04_Order_{0}_DomainRecord";
}

public class AcmeConst
{
    public const string ValidStatus = "valid";
    public const string InvalidStatus = "invalid";
    public const string PendingStatus = "pending";

    public const string RsaKeyType = "rsa";
    public const string EcKeyType = "ec";

    public static IReadOnlyDictionary<string, int> DefaultAlgorKeySizeMap = new Dictionary<string, int>
    {
        { RsaKeyType, 2048  },
        { EcKeyType, 256 }
    };
}