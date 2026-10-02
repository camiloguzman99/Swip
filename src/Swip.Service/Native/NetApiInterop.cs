using System.Runtime.InteropServices;

namespace Swip.Service.Native;

/// <summary>
/// P/Invoke a netapi32 para enumerar las cuentas de usuario locales del equipo.
/// Permite mostrar un gato por usuario, tenga o no una sesión abierta.
/// </summary>
internal static class NetApiInterop
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct USER_INFO_1
    {
        public string usri1_name;
        public string usri1_password;
        public int usri1_password_age;
        public int usri1_priv;
        public string usri1_home_dir;
        public string usri1_comment;
        public int usri1_flags;
        public string usri1_script_path;
    }

    public const int FILTER_NORMAL_ACCOUNT = 0x0002;
    public const int MAX_PREFERRED_LENGTH = -1;
    public const int UF_ACCOUNTDISABLE = 0x0002;
    public const int NERR_Success = 0;
    public const int ERROR_MORE_DATA = 234;

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int NetUserEnum(
        string? servername,
        int level,
        int filter,
        out IntPtr bufptr,
        int prefmaxlen,
        out int entriesread,
        out int totalentries,
        ref int resumehandle);

    [DllImport("netapi32.dll")]
    public static extern int NetApiBufferFree(IntPtr buffer);

    /// <summary>Nombres de cuentas locales habilitadas, excluyendo integradas y de máquina.</summary>
    public static List<string> EnumerateEnabledLocalUsers()
    {
        var users = new List<string>();
        int resume = 0;

        do
        {
            int status = NetUserEnum(null, 1, FILTER_NORMAL_ACCOUNT, out IntPtr buffer,
                MAX_PREFERRED_LENGTH, out int read, out _, ref resume);

            if (status != NERR_Success && status != ERROR_MORE_DATA)
                break;

            try
            {
                int size = Marshal.SizeOf<USER_INFO_1>();
                IntPtr current = buffer;
                for (int i = 0; i < read; i++)
                {
                    var info = Marshal.PtrToStructure<USER_INFO_1>(current);
                    current += size;

                    if ((info.usri1_flags & UF_ACCOUNTDISABLE) != 0)
                        continue;
                    if (IsBuiltInOrMachine(info.usri1_name))
                        continue;

                    users.Add(info.usri1_name);
                }
            }
            finally
            {
                NetApiBufferFree(buffer);
            }

            if (status != ERROR_MORE_DATA)
                break;
        }
        while (true);

        return users;
    }

    private static bool IsBuiltInOrMachine(string name)
    {
        if (string.IsNullOrEmpty(name)) return true;
        if (name.EndsWith("$", StringComparison.Ordinal)) return true; // cuentas de máquina
        return name.ToLowerInvariant() switch
        {
            "administrator" => true,
            "administrador" => true,
            "guest" => true,
            "invitado" => true,
            "defaultaccount" => true,
            "wdagutilityaccount" => true,
            _ => false,
        };
    }
}
