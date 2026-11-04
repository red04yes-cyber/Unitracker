using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
namespace UniToStudyHub.Services;
public static class WindowsTokenVault
{
 const int UiForbidden=1; static readonly string PathToken=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"UniToStudyHub","token.bin");
 [StructLayout(LayoutKind.Sequential)] struct Blob{public int Size;public IntPtr Data;}
 [DllImport("crypt32.dll",SetLastError=true,CharSet=CharSet.Unicode)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool CryptProtectData(ref Blob input,string? description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
 [DllImport("crypt32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] static extern bool CryptUnprotectData(ref Blob input,IntPtr description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
 [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);
 public static void Save(string token){if(string.IsNullOrWhiteSpace(token))throw new ArgumentException("Token vuoto.");var input=Make(Encoding.UTF8.GetBytes(token.Trim()));try{if(!CryptProtectData(ref input,"UniToStudyHub token",IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,UiForbidden,out var output))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());try{var b=new byte[output.Size];Marshal.Copy(output.Data,b,0,b.Length);Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathToken)!);File.WriteAllBytes(PathToken,b);}finally{if(output.Data!=IntPtr.Zero)LocalFree(output.Data);}}finally{Free(ref input);}}
 public static string? Load(){if(!File.Exists(PathToken))return null;var input=Make(File.ReadAllBytes(PathToken));try{if(!CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,UiForbidden,out var output))throw new SecurityException("Token non decifrabile per l'utente Windows corrente.");try{var b=new byte[output.Size];Marshal.Copy(output.Data,b,0,b.Length);return Encoding.UTF8.GetString(b);}finally{if(output.Data!=IntPtr.Zero)LocalFree(output.Data);}}finally{Free(ref input);}}
 static Blob Make(byte[] b){var p=Marshal.AllocHGlobal(b.Length);Marshal.Copy(b,0,p,b.Length);return new Blob{Size=b.Length,Data=p};} static void Free(ref Blob b){if(b.Data!=IntPtr.Zero)Marshal.FreeHGlobal(b.Data);b=default;}
}