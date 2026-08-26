using System.Runtime.InteropServices;
using System.Text;

namespace IUSpacesHelper;

class Program
{
    private const string DllName = "IUSpaces.dll";

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GetIUPropertyStoreDelegate(out IntPtr propertyStore);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void CheckSpacesVersionDelegate();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void SetLoggerDelegate(IntPtr logger);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void CreateNewPoolDelegate(IntPtr propertyStore, out Guid poolId);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void CreateNewSpaceDelegate(IntPtr propertyStore);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GetPoolIdFromPoolNameDelegate([MarshalAs(UnmanagedType.LPWStr)] string poolName, out Guid poolId);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void SetStorageSpacesFlagsDelegate(IntPtr guid16, IntPtr spaceListStruct);

    [UnmanagedFunctionPointer(CallingConvention.FastCall)]
    private delegate void SetPropertyDelegate(IntPtr thisPtr, IntPtr propertyValueStruct);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyValue
    {
        public IntPtr Unknown0;
        public IntPtr Unknown4;
        public int Type;
        public uint PropId;
        public IntPtr DataPtr;
        public int DataSize;
    }

    private enum PropType : int
    {
        Byte = 0,
        Guid = 1,
        Blob = 2,
        Short = 3,
        UInt = 4,
        ULong = 5,
        TwentyBytes = 6
    }

    static IntPtr _hModule;

    static T GetFunc<T>(string name) where T : Delegate
    {
        IntPtr addr = GetProcAddress(_hModule, name);
        if (addr == IntPtr.Zero) throw new Exception($"Function {name} not found");
        return Marshal.GetDelegateForFunctionPointer<T>(addr);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll")]
    static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("IUSpacesWrapper.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private static extern int IUSpaces_CheckVersion();

    [DllImport("IUSpacesWrapper.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private static extern int IUSpaces_CreatePool(string poolName, string diskPath, out Guid poolId);

    [DllImport("IUSpacesWrapper.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private static extern int IUSpaces_CreateSpace(ref Guid poolId, string spaceName, ulong sizeBytes);

    [DllImport("IUSpacesWrapper.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int IUSpaces_TestProps();

    [DllImport("IUSpacesWrapper.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private static extern int IUSpaces_DiagnosePool(string poolName, string diskPath, out Guid poolId);

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            if (args.Length == 0) { PrintUsage(); return 0; }
            string cmd = args[0].ToLower();
            switch (cmd)
            {
                case "version": return CmdVersion();
                case "createpool": return CmdCreatePool(args);
                case "createspace": return CmdCreateSpace(args);
                case "getpoolid": return CmdGetPoolId(args);
                case "test": return CmdTest();
                case "props": return CmdPropsTest();
                case "testprops": return CmdTestProps();
                case "diagnose": return CmdDiagnose(args);
                default:
                    Console.WriteLine($"Unknown command: {cmd}");
                    PrintUsage();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
            return -1;
        }
    }

    static void PrintUsage()
    {
        Console.WriteLine("IUSpacesHelper - WSK IUSpaces.dll wrapper (WinStory 2026)");
        Console.WriteLine("");
        Console.WriteLine("Usage:");
        Console.WriteLine("  IUSpacesHelper version");
        Console.WriteLine("  IUSpacesHelper createpool <poolName> <diskPath>");
        Console.WriteLine("  IUSpacesHelper createspace <poolId> <spaceName> <sizeMB>");
        Console.WriteLine("  IUSpacesHelper getpoolid <poolName>");
        Console.WriteLine("  IUSpacesHelper test");
        Console.WriteLine("  IUSpacesHelper props");
    }

    static bool LoadDll()
    {
        _hModule = LoadLibrary(DllName);
        if (_hModule == IntPtr.Zero)
        {
            string fullPath = Path.Combine(AppContext.BaseDirectory, DllName);
            _hModule = LoadLibrary(fullPath);
        }
        return _hModule != IntPtr.Zero;
    }

    static int CmdVersion()
    {
        if (!LoadDll()) { Console.WriteLine("FAIL: LoadLibrary"); return 1; }
        try
        {
            var fn = GetFunc<CheckSpacesVersionDelegate>("CheckSpacesVersion");
            fn();
            Console.WriteLine("OK: CheckSpacesVersion passed");
        }
        catch (Exception ex) { Console.WriteLine($"FAIL: {ex.Message}"); }
        FreeLibrary(_hModule);
        return 0;
    }

    static int CmdTest()
    {
        Console.WriteLine("=== IUSpaces.dll Test ===");
        if (!LoadDll()) { Console.WriteLine("FAIL: LoadLibrary"); return 1; }
        Console.WriteLine($"Loaded: 0x{_hModule.ToInt64():X}");

        string[] names = { "CheckSpacesVersion","CreateNewPool","CreateNewSpace",
            "EnumerateSpacesFilteredInternal","EnumerateSpacesInternal","GetIUPropertyStore",
            "GetPoolIdFromPoolName","OpenPool","OpenSpace","SetLogger","SetStorageSpacesFlags" };
        foreach (string n in names)
        {
            IntPtr a = GetProcAddress(_hModule, n);
            Console.WriteLine($"  {n}: 0x{a.ToInt64():X}");
        }

        try
        {
            var fn = GetFunc<CheckSpacesVersionDelegate>("CheckSpacesVersion");
            fn();
            Console.WriteLine("CheckSpacesVersion: OK");
        }
        catch (Exception ex) { Console.WriteLine($"CheckSpacesVersion: FAIL {ex.Message}"); }

        FreeLibrary(_hModule);
        return 0;
    }

    static int CmdPropsTest()
    {
        Console.WriteLine("=== Property Store Diagnostic ===");
        if (!LoadDll()) { Console.WriteLine("FAIL: LoadLibrary"); return 1; }
        try
        {
            var getStore = GetFunc<GetIUPropertyStoreDelegate>("GetIUPropertyStore");
            IntPtr store;
            getStore(out store);
            Console.WriteLine($"PropertyStore: 0x{store.ToInt64():X}");
            Console.WriteLine($"DLL base: 0x{_hModule.ToInt64():X}");

            Console.WriteLine("--- Main object pointers (non-zero only) ---");
            for (int i = 0; i < 0x50; i += 4)
            {
                try
                {
                    int val = Marshal.ReadInt32(store, i);
                    if (val != 0)
                        Console.WriteLine($"  +0x{i:X2}: 0x{val:X8}");
                }
                catch { break; }
            }

            Console.WriteLine("--- Main vtable (RVA 0x1E90) first 0x40 bytes ---");
            try
            {
                int mainVtable = Marshal.ReadInt32(store, 0);
                for (int i = 0; i < 0x40; i += 4)
                {
                    int val = Marshal.ReadInt32((IntPtr)mainVtable, i);
                    Console.WriteLine($"  vt+0x{i:X2}: 0x{val:X8}");
                }
                int off1 = Marshal.ReadInt32((IntPtr)mainVtable, 4);
                int off2 = Marshal.ReadInt32((IntPtr)mainVtable, 8);
                Console.WriteLine($"  Dynamic subobj offsets: +0x{off1:X}, +0x{off2:X}");
                if (off1 > 0 && off1 < 0x100)
                {
                    int sub1 = Marshal.ReadInt32(store, off1);
                    Console.WriteLine($"  +0x{off1:X} vtable: 0x{sub1:X8} (RVA 0x{sub1 - _hModule.ToInt32():X})");
                }
                if (off2 > 0 && off2 < 0x100)
                {
                    int sub2 = Marshal.ReadInt32(store, off2);
                    Console.WriteLine($"  +0x{off2:X} vtable: 0x{sub2:X8} (RVA 0x{sub2 - _hModule.ToInt32():X})");
                }
            }
            catch (Exception ex) { Console.WriteLine($"  vtable read error: {ex.Message}"); }
        }
        catch (Exception ex) { Console.WriteLine($"FAIL: {ex.Message}"); }
        FreeLibrary(_hModule);
        return 0;
    }

    static int CmdTestProps()
    {
        Console.WriteLine("Testing wrapper SetProperty...");
        try
        {
            int ret = IUSpaces_TestProps();
            Console.WriteLine($"TestProps returned: {ret}");
            return ret;
        }
        catch (DllNotFoundException ex) { Console.WriteLine($"DllNotFound: {ex.Message}"); return -10; }
        catch (BadImageFormatException ex) { Console.WriteLine($"BadImageFormat: {ex.Message}"); return -11; }
        catch (Exception ex) { Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}"); return -1; }
    }

    static int CmdDiagnose(string[] args)
    {
        string poolName = args.Length > 1 ? args[1] : "TestPool";
        string diskPath = args.Length > 2 ? args[2] : "test";
        Console.WriteLine($"Diagnosing CreateNewPool... poolName={poolName} diskPath={diskPath}");
        try
        {
            Guid poolId;
            int ret = IUSpaces_DiagnosePool(poolName, diskPath, out poolId);
            Console.WriteLine($"Diagnose returned: {ret}");
            Console.WriteLine($"Log file: %TEMP%\\iuspaces_wrapper.log");
            return ret;
        }
        catch (Exception ex) { Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}"); return -1; }
    }

    static string GetFuncNameByRva(long rva)
    {
        switch (rva)
        {
            case 0xcfe0: return "SetProperty(FUN_1000cfe0)";
            case 0xcdd0: return "GetProperty(FUN_1000cdd0)";
            case 0xccf0: return "Clear(FUN_1000ccf0)";
            case 0xd1e0: return "RemoveProperty(FUN_1000d1e0)";
            case 0x1eb40: return "purecall";
            default: return "";
        }
    }

    static int CmdGetPoolId(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("Usage: getpoolid <poolName>"); return 1; }
        if (!LoadDll()) { Console.WriteLine("FAIL: LoadLibrary"); return 1; }
        try
        {
            var fn = GetFunc<GetPoolIdFromPoolNameDelegate>("GetPoolIdFromPoolName");
            Guid poolId;
            fn(args[1], out poolId);
            Console.WriteLine($"PoolId: {poolId}");
        }
        catch (Exception ex) { Console.WriteLine($"FAIL: {ex.Message}"); }
        FreeLibrary(_hModule);
        return 0;
    }

    static int CmdCreatePool(string[] args)
    {
        if (args.Length < 3) { Console.WriteLine("Usage: createpool <poolName> <diskPath>"); return 1; }
        string poolName = args[1];
        string diskPath = args[2];
        Console.WriteLine($"Creating pool: {poolName} on {diskPath}");
        try
        {
            Guid poolId;
            int ret = IUSpaces_CreatePool(poolName, diskPath, out poolId);
            if (ret == 0)
                Console.WriteLine($"Pool created: {poolId}");
            else
                Console.WriteLine($"CreatePool failed with code {ret}");
            return ret;
        }
        catch (Exception ex) { Console.WriteLine($"FAIL: {ex.Message}"); return -1; }
    }

    static int CmdCreateSpace(string[] args)
    {
        if (args.Length < 4) { Console.WriteLine("Usage: createspace <poolId> <spaceName> <sizeMB>"); return 1; }
        Guid poolId = Guid.Parse(args[1]);
        string spaceName = args[2];
        long sizeMB = long.Parse(args[3]);
        Console.WriteLine($"Creating space: {spaceName} in pool {poolId}, size={sizeMB}MB");
        try
        {
            int ret = IUSpaces_CreateSpace(ref poolId, spaceName, (ulong)(sizeMB * 1024 * 1024));
            if (ret == 0)
                Console.WriteLine("Space created.");
            else
                Console.WriteLine($"CreateSpace failed with code {ret}");
            return ret;
        }
        catch (Exception ex) { Console.WriteLine($"FAIL: {ex.Message}"); return -1; }
    }

    static IntPtr GetSubObject(IntPtr store, int offset)
    {
        return IntPtr.Add(store, offset);
    }

    static SetPropertyDelegate GetSetProperty(IntPtr store)
    {
        IntPtr subObj = IntPtr.Add(store, 0x40);
        IntPtr vtable = Marshal.ReadIntPtr(subObj);
        IntPtr addr = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<SetPropertyDelegate>(addr);
    }

    static void SetPropertyString(SetPropertyDelegate setProp, IntPtr store, uint propId, string value)
    {
        IntPtr subObj = IntPtr.Add(store, 0x40);
        byte[] bytes = Encoding.Unicode.GetBytes(value + "\0");
        IntPtr dataPtr = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, dataPtr, bytes.Length);
        SetPropertyBlob(setProp, subObj, propId, dataPtr, bytes.Length);
        Marshal.FreeHGlobal(dataPtr);
    }

    static void SetPropertyGuid(SetPropertyDelegate setProp, IntPtr store, uint propId, Guid value)
    {
        IntPtr subObj = IntPtr.Add(store, 0x40);
        byte[] bytes = value.ToByteArray();
        IntPtr dataPtr = Marshal.AllocHGlobal(16);
        Marshal.Copy(bytes, 0, dataPtr, 16);
        SetPropertyBlob(setProp, subObj, propId, dataPtr, 16);
        Marshal.FreeHGlobal(dataPtr);
    }

    static void SetPropertyULong(SetPropertyDelegate setProp, IntPtr store, uint propId, ulong value)
    {
        IntPtr subObj = IntPtr.Add(store, 0x40);
        IntPtr dataPtr = Marshal.AllocHGlobal(8);
        Marshal.WriteInt64(dataPtr, (long)value);
        SetPropertyBlob(setProp, subObj, propId, dataPtr, 8);
        Marshal.FreeHGlobal(dataPtr);
    }

    static void SetPropertyBlob(SetPropertyDelegate setProp, IntPtr subObj, uint propId, IntPtr dataPtr, int dataSize)
    {
        PropertyValue pv = new PropertyValue
        {
            Type = (int)PropType.Blob,
            PropId = propId,
            DataPtr = dataPtr,
            DataSize = dataSize
        };
        IntPtr pvPtr = Marshal.AllocHGlobal(Marshal.SizeOf<PropertyValue>());
        Marshal.StructureToPtr(pv, pvPtr, false);
        setProp(subObj, pvPtr);
        Marshal.FreeHGlobal(pvPtr);
    }
}
