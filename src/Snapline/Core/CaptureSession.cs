using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;

namespace Snapline.Core;

public sealed class OwnedCapture
{
    public string Folder { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string SessionKey { get; set; } = "";
    public string Day { get; set; } = "";
}

internal static class CaptureSession
{
    [DllImport("advapi32.dll", SetLastError=true)]
    private static extern bool GetTokenInformation(IntPtr token, int kind, IntPtr data, int size, out int returned);
    private static readonly string LoginKey = ReadLoginKey();
    public static string CurrentKey => LoginKey;
    public static string Today => DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    private static string ReadLoginKey()
    {
        using var identity=WindowsIdentity.GetCurrent();
        var buffer=Marshal.AllocHGlobal(128);
        try
        {
            if(!GetTokenInformation(identity.AccessToken.DangerousGetHandle(),10,buffer,128,out int size) || size<16)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return $"logon-{unchecked((uint)Marshal.ReadInt32(buffer,12)):X8}{unchecked((uint)Marshal.ReadInt32(buffer,8)):X8}";
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    public static string Hash(string path)
    {
        using var stream=File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    public static void Own(string path)
    {
        if(Settings.ReadOnly) return;
        Settings.Current.OwnedCaptures[Path.GetFullPath(path)]=new OwnedCapture {Folder=Path.GetFullPath(Inbox.Folder),Sha256=Hash(path),SessionKey=CurrentKey,Day=Today};
        Settings.Current.Save();
    }
    public static void RefreshHash(string path)
    {
        if(Settings.ReadOnly || !Settings.Current.OwnedCaptures.TryGetValue(Path.GetFullPath(path),out var owned)) return;
        owned.Sha256=Hash(path);Settings.Current.Save();
    }
    public static bool Refresh(Settings settings,string key,string day,Func<string,bool> recycle)
    {
        if(settings.CaptureSessionKey==key && settings.CaptureSessionDay==day) return false;
        // A legacy profile has no ownership manifest: clear only the hanging
        // state, never infer ownership from a folder name or filename alone.
        foreach(var (path,owned) in settings.OwnedCaptures.ToList())
        {
            if(owned.SessionKey==key && owned.Day==day) continue;
            try
            {
                if(!File.Exists(path)) {settings.OwnedCaptures.Remove(path);continue;}
                if(!CanRecycle(path,owned)) continue;
                if(recycle(path)) {settings.OwnedCaptures.Remove(path);settings.Notes.Remove(path);}
            }
            catch { /* Busy, replaced or unreadable files are retained. */ }
        }
        settings.Pegged.Clear();settings.PinnedPaths.Clear();settings.Spots.Clear();
        settings.CaptureSessionKey=key;settings.CaptureSessionDay=day;
        return true;
    }
    internal static bool CanRecycle(string path,OwnedCapture owned)
    {
        var full=Path.GetFullPath(path);
        if(!string.Equals(Path.GetDirectoryName(full),Path.GetFullPath(owned.Folder).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)) return false;
        FileSystemInfo? info=new FileInfo(full);
        while(info is not null)
        {
            if((info.Attributes&FileAttributes.ReparsePoint)!=0) return false;
            info=info is DirectoryInfo d ? d.Parent : ((FileInfo)info).Directory;
        }
        return !string.IsNullOrEmpty(owned.Sha256) && Hash(full)==owned.Sha256;
    }
}
