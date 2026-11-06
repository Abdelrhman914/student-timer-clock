using System.Security.Cryptography;

namespace StudentTimerClock.Security;

/// <summary>
/// Detects modification of data.json by anything other than this app.
/// Every save writes data.json.seal = HMAC-SHA256(data.json) with a random 256-bit key that is encrypted with
/// Windows DPAPI for the current user (seal.key). On load a missing or wrong seal means "changed outside the app":
/// the data is still loaded (the user's work is never thrown away) but every action that could run code is quarantined.
/// </summary>
public static class DataSeal
{
    static readonly byte[] Entropy = "StudentTimerClock/DataSeal/v1"u8.ToArray();
    static byte[] _key;

    static string KeyFile => Path.Combine(AppState.DataDir, "seal.key");
    public static string SealFile(string dataFile) => dataFile + ".seal";

    public enum State { Valid, FirstUse, Modified }

    static byte[] Key(bool create)
    {
        if (_key != null) return _key;
        try
        {
            if (File.Exists(KeyFile))
            {
                _key = ProtectedData.Unprotect(File.ReadAllBytes(KeyFile), Entropy, DataProtectionScope.CurrentUser);
                if (_key.Length == 32) return _key;
                _key = null;
            }
        }
        catch { _key = null; } // e.g. data folder copied from another PC/user: the key can't be decrypted here
        if (!create) return null;
        _key = RandomNumberGenerator.GetBytes(32);
        try
        {
            Directory.CreateDirectory(AppState.DataDir);
            File.WriteAllBytes(KeyFile, ProtectedData.Protect(_key, Entropy, DataProtectionScope.CurrentUser));
            try { File.SetAttributes(KeyFile, FileAttributes.Hidden); } catch { }
        }
        catch { }
        return _key;
    }

    static byte[] Mac(byte[] data, byte[] key) => HMACSHA256.HashData(key, data);

    /// <summary>Checks the seal of the bytes that were just read from <paramref name="dataFile"/>.</summary>
    public static State Check(string dataFile, byte[] content)
    {
        bool keyExisted = File.Exists(KeyFile);
        var key = Key(create: false);
        string seal = SealFile(dataFile);
        if (key == null)
        {
            // first run of a sealing version (upgrade from an older version) -> trust on first use
            if (!keyExisted && !File.Exists(seal)) return State.FirstUse;
            return State.Modified; // key exists but unreadable (other PC/user) or seal without key
        }
        if (!File.Exists(seal)) return State.Modified;
        try
        {
            var expected = Convert.FromBase64String(File.ReadAllText(seal).Trim());
            return CryptographicOperations.FixedTimeEquals(expected, Mac(content, key)) ? State.Valid : State.Modified;
        }
        catch { return State.Modified; }
    }

    /// <summary>Writes the seal for the bytes that were just written to <paramref name="dataFile"/>.</summary>
    public static void Write(string dataFile, byte[] content)
    {
        try
        {
            var key = Key(create: true);
            if (key == null) return;
            string seal = SealFile(dataFile), tmp = seal + ".tmp";
            File.WriteAllText(tmp, Convert.ToBase64String(Mac(content, key)));
            if (File.Exists(seal)) File.Replace(tmp, seal, null); else File.Move(tmp, seal);
        }
        catch { }
    }
}
