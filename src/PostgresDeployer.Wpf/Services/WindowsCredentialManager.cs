namespace PostgresDeployer.Wpf.Services;

using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// 使用 Windows 認證管理員安全儲存資料庫密碼，不寫入設定檔。
/// </summary>
public static class WindowsCredentialManager
{
    private const string AppPrefix = "PostgresDeployer:";
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    /// <summary>產生認證的唯一鍵值（以連線資訊組合）。</summary>
    public static string CredentialKey(string host, int port, string database, string username)
        => $"{AppPrefix}{host}:{port}/{database}#{username}";

    /// <summary>將密碼儲存至 Windows 認證管理員。若密碼為空則刪除該認證。</summary>
    public static void Save(string key, string password)
    {
        try
        {
            if (string.IsNullOrEmpty(password))
            {
                CredDelete(key, CredTypeGeneric, 0);
                return;
            }

            var blob = Encoding.UTF8.GetBytes(password);
            var blobPtr = Marshal.AllocHGlobal(blob.Length);
            var targetPtr = Marshal.StringToHGlobalUni(key);
            try
            {
                Marshal.Copy(blob, 0, blobPtr, blob.Length);
                var cred = new CREDENTIAL
                {
                    Type = CredTypeGeneric,
                    TargetName = targetPtr,
                    CredentialBlobSize = (uint)blob.Length,
                    CredentialBlob = blobPtr,
                    Persist = CredPersistLocalMachine,
                };
                CredWrite(ref cred, 0);
            }
            finally
            {
                Marshal.FreeHGlobal(blobPtr);
                Marshal.FreeHGlobal(targetPtr);
            }
        }
        catch
        {
            // 若無法存取 Credential Manager，靜默失敗
        }
    }

    /// <summary>從 Windows 認證管理員讀取密碼；若不存在則回傳空字串。</summary>
    public static string Load(string key)
    {
        try
        {
            if (!CredRead(key, CredTypeGeneric, 0, out var ptr)) return "";
            try
            {
                var cred = Marshal.PtrToStructure<CREDENTIAL>(ptr);
                if (cred.CredentialBlobSize == 0) return "";
                var blob = new byte[cred.CredentialBlobSize];
                Marshal.Copy(cred.CredentialBlob, blob, 0, (int)cred.CredentialBlobSize);
                return Encoding.UTF8.GetString(blob);
            }
            finally
            {
                CredFree(ptr);
            }
        }
        catch
        {
            return "";
        }
    }
}
