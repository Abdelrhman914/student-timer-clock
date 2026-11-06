using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace StudentTimerClock.Security;

/// <summary>
/// Self-contained Authenticode verification (no dependency on the Windows certificate store):
///  1. parses the PE file and computes the SHA-256 Authenticode image digest
///     (whole file except the checksum field, the certificate-table directory entry and the certificate table);
///  2. decodes the embedded PKCS#7 SignedData, checks that it is SPC_INDIRECT_DATA with a SHA-256 digest equal to (1);
///  3. verifies the CMS signature cryptographically with the signer certificate;
///  4. pins the signer certificate (SHA-256 of its DER encoding) to the value compiled into this program.
/// In the same single pass it also computes the plain SHA-256 of the complete file.
/// </summary>
public static class AuthenticodeVerifier
{
    const string SpcIndirectDataOid = "1.3.6.1.4.1.311.2.1.4";
    const string Sha256Oid = "2.16.840.1.101.3.4.2.1";
    const string CodeSigningEku = "1.3.6.1.5.5.7.3.3";
    const string SigningTimeOid = "1.2.840.113549.1.9.5";

    public sealed class Result
    {
        public bool Ok;
        public string Error;
        public string FileSha256;          // SHA-256 of the complete file (hex, upper case)
        public string AuthenticodeSha256;  // signed image digest (hex)
        public X509Certificate2 Signer;
        public string SignerSha256;        // SHA-256 thumbprint of the signer certificate
        public DateTime? SigningTime;
    }

    static Result Fail(Result r, string e) { r.Ok = false; r.Error = e; return r; }

    public static string Hex(byte[] b) => Convert.ToHexString(b);

    public static Result Verify(string path, string pinnedSignerSha256)
    {
        var r = new Result();
        byte[] file;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
            if (fs.Length > 1L << 30 || fs.Length < 1024) return Fail(r, "unexpected executable size");
            file = new byte[fs.Length];
            fs.ReadExactly(file);
        }
        catch (Exception ex) { return Fail(r, "cannot read executable: " + ex.Message); }

        r.FileSha256 = Hex(SHA256.HashData(file));

        // ---- PE layout
        int peOff, optOff, checksumOff, certDirOff;
        uint certOff, certSize;
        try
        {
            if (file[0] != 'M' || file[1] != 'Z') return Fail(r, "not a PE file");
            peOff = BitConverter.ToInt32(file, 0x3C);
            if (peOff <= 0 || peOff > file.Length - 256 || BitConverter.ToUInt32(file, peOff) != 0x00004550) return Fail(r, "bad PE header");
            optOff = peOff + 24;
            ushort magic = BitConverter.ToUInt16(file, optOff);
            int dirBase = magic switch { 0x10B => optOff + 96, 0x20B => optOff + 112, _ => -1 };
            if (dirBase < 0) return Fail(r, "unknown optional header");
            uint numDirs = BitConverter.ToUInt32(file, dirBase - 4);
            if (numDirs < 5) return Fail(r, "no certificate directory");
            checksumOff = optOff + 64;
            certDirOff = dirBase + 4 * 8;
            certOff = BitConverter.ToUInt32(file, certDirOff);
            certSize = BitConverter.ToUInt32(file, certDirOff + 4);
        }
        catch { return Fail(r, "malformed PE file"); }
        if (certOff == 0 || certSize == 0) return Fail(r, "the executable is not digitally signed");
        if (certOff < certDirOff + 8 || (long)certOff + certSize != file.Length)
            return Fail(r, "the certificate table is not at the end of the file (data was appended or moved)");

        // ---- Authenticode image digest
        byte[] imageDigest;
        using (var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            h.AppendData(file, 0, checksumOff);
            h.AppendData(file, checksumOff + 4, certDirOff - (checksumOff + 4));
            h.AppendData(file, certDirOff + 8, (int)certOff - (certDirOff + 8));
            imageDigest = h.GetHashAndReset();
        }
        r.AuthenticodeSha256 = Hex(imageDigest);

        // ---- WIN_CERTIFICATE -> PKCS#7
        int len = BitConverter.ToInt32(file, (int)certOff);
        ushort rev = BitConverter.ToUInt16(file, (int)certOff + 4), type = BitConverter.ToUInt16(file, (int)certOff + 6);
        if (len < 16 || len > certSize || rev != 0x0200 || type != 0x0002) return Fail(r, "unsupported signature format");
        var pkcs7 = file.AsSpan((int)certOff + 8, len - 8);
        // trim DER padding after the outer SEQUENCE
        try
        {
            var reader = new AsnReader(pkcs7.ToArray(), AsnEncodingRules.BER);
            pkcs7 = reader.PeekEncodedValue().ToArray();
        }
        catch { return Fail(r, "corrupt signature block"); }
        // there must be exactly one WIN_CERTIFICATE (8-byte aligned)
        int aligned = (len + 7) & ~7;
        if (aligned < certSize && file.AsSpan((int)certOff + aligned, (int)certSize - aligned).IndexOfAnyExcept((byte)0) >= 0)
            return Fail(r, "additional data was hidden in the certificate table");

        var cms = new SignedCms();
        try { cms.Decode(pkcs7.ToArray()); }
        catch (Exception ex) { return Fail(r, "cannot decode signature: " + ex.Message); }
        if (cms.ContentInfo.ContentType.Value != SpcIndirectDataOid) return Fail(r, "not an Authenticode signature");
        if (cms.SignerInfos.Count != 1) return Fail(r, "unexpected number of signers");

        // ---- SpcIndirectDataContent ::= SEQUENCE { data SpcAttributeTypeAndOptionalValue, messageDigest DigestInfo }
        byte[] signedDigest; string digestAlg;
        try
        {
            var content = cms.ContentInfo.Content;
            // .NET may hand us either the full SEQUENCE or only its contents; accept both
            AsnReader seq;
            var outer = new AsnReader(content, AsnEncodingRules.BER);
            if (outer.PeekTag().HasSameClassAndValue(Asn1Tag.Sequence) && content.Length == outer.PeekEncodedValue().Length)
            {
                seq = outer.ReadSequence();
                // contents-only form starts with the SpcAttributeTypeAndOptionalValue SEQUENCE too; detect by structure
                var probe = seq.Clone();
                if (!probe.PeekTag().HasSameClassAndValue(Asn1Tag.Sequence)) seq = new AsnReader(content, AsnEncodingRules.BER);
            }
            else seq = new AsnReader(content, AsnEncodingRules.BER);
            seq.ReadEncodedValue();                         // SpcAttributeTypeAndOptionalValue
            var digestInfo = seq.ReadSequence();
            var algId = digestInfo.ReadSequence();
            digestAlg = algId.ReadObjectIdentifier();
            signedDigest = digestInfo.ReadOctetString();
        }
        catch (Exception ex) { return Fail(r, "cannot parse signed content: " + ex.Message); }
        if (digestAlg != Sha256Oid) return Fail(r, "the signature does not use SHA-256");
        if (!CryptographicOperations.FixedTimeEquals(signedDigest, imageDigest))
            return Fail(r, "the executable was modified after it was signed (Authenticode digest mismatch)");

        // ---- cryptographic signature check (signed attributes incl. messageDigest of the content)
        var si = cms.SignerInfos[0];
        try { si.CheckSignature(verifySignatureOnly: true); }
        catch (Exception ex) { return Fail(r, "the digital signature is invalid: " + ex.Message); }
        var signer = si.Certificate;
        if (signer == null) return Fail(r, "signer certificate missing");
        r.Signer = signer;
        r.SignerSha256 = Hex(SHA256.HashData(signer.RawData));

        // ---- certificate pinning + purpose
        if (string.IsNullOrEmpty(pinnedSignerSha256) || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(pinnedSignerSha256), Convert.FromHexString(r.SignerSha256)))
            return Fail(r, "the executable is signed by an unknown certificate (" + r.SignerSha256[..16] + "…)");
        bool eku = signer.Extensions.OfType<X509EnhancedKeyUsageExtension>().Any(e => e.EnhancedKeyUsages.Cast<Oid>().Any(o => o.Value == CodeSigningEku));
        if (!eku) return Fail(r, "the signer certificate is not a code-signing certificate");

        foreach (var a in si.SignedAttributes)
            if (a.Oid.Value == SigningTimeOid && a.Values.Count > 0)
                try { r.SigningTime = new Pkcs9SigningTime(a.Values[0].RawData).SigningTime; } catch { }

        r.Ok = true;
        return r;
    }

    /// <summary>Verifies an RSA/SHA-256 (PKCS#1 v1.5) detached signature with the public key of <paramref name="cert"/>.</summary>
    public static bool VerifyDetached(byte[] data, byte[] signature, X509Certificate2 cert)
    {
        try
        {
            using var rsa = cert.GetRSAPublicKey();
            return rsa != null && rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch { return false; }
    }
}
