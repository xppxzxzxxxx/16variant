using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace DrivingSchool.Api;

public static class LoopbackCertificate
{
    public static X509Certificate2 Create()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=DrivingSchool", key, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));
        var now = DateTimeOffset.UtcNow;
        using var generated = request.CreateSelfSigned(now.AddMinutes(-5), now.AddDays(2));
        string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        byte[] pfx = generated.Export(X509ContentType.Pfx, password);
        try
        {
            // Schannel needs an imported private-key handle for the server certificate.
#if NET8_0
            return new X509Certificate2(pfx, password, X509KeyStorageFlags.DefaultKeySet);
#else
            return X509CertificateLoader.LoadPkcs12(pfx, password, X509KeyStorageFlags.DefaultKeySet);
#endif
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pfx);
        }
    }
}
