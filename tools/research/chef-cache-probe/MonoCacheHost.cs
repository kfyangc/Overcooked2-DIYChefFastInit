using System;
using System.IO;
using System.Runtime.InteropServices;

class MonoCacheHost
{
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr LoadLibrary(string path);
    [DllImport("kernel32.dll", CharSet=CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module, string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SetPath([MarshalAs(UnmanagedType.LPStr)] string path);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SetDirs(string assemblies, string configuration);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr Init(string domain, string version);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr OpenAssembly(IntPtr domain, string path);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr GetImage(IntPtr assembly);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr GetClass(IntPtr image, string ns, string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr GetMethod(IntPtr klass, string name, int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr Invoke(IntPtr method, IntPtr obj, IntPtr args, out IntPtr exception);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr MonoToString(IntPtr obj, out IntPtr exception);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr Utf8(IntPtr value);
    static IntPtr mono;
    static Delegate Api(string name, Type type)
    {
        IntPtr address = GetProcAddress(mono, name);
        if (address == IntPtr.Zero) throw new Exception("Missing Mono API: " + name);
        return Marshal.GetDelegateForFunctionPointer(address, type);
    }
    static int Main(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Expected game directory and managed probe DLL path");
        string game = Path.GetFullPath(args[0]);
        mono = LoadLibrary(game + @"\Overcooked2_Data\Mono\EmbedRuntime\mono.dll");
        if (mono == IntPtr.Zero) throw new Exception("Cannot load game Mono runtime");
        ((SetDirs)Api("mono_set_dirs",typeof(SetDirs)))(game + @"\Overcooked2_Data\Managed",game + @"\Overcooked2_Data\Mono\etc");
        ((SetPath)Api("mono_set_assemblies_path",typeof(SetPath)))(game + @"\Overcooked2_Data\Managed");
        IntPtr domain = ((Init)Api("mono_jit_init_version",typeof(Init)))("DIYChef offline cache probe","v2.0.50727");
        if (domain == IntPtr.Zero) throw new Exception("Mono initialization failed");
        IntPtr assembly = ((OpenAssembly)Api("mono_domain_assembly_open",typeof(OpenAssembly)))(domain,Path.GetFullPath(args[1]));
        if (assembly == IntPtr.Zero) throw new Exception("Cannot open benchmark assembly");
        IntPtr image = ((GetImage)Api("mono_assembly_get_image",typeof(GetImage)))(assembly);
        IntPtr klass = ((GetClass)Api("mono_class_from_name",typeof(GetClass)))(image,"OC2DIYChef.Research","MonoCacheProbe");
        IntPtr method = ((GetMethod)Api("mono_class_get_method_from_name",typeof(GetMethod)))(klass,"Run",0);
        IntPtr exception;
        ((Invoke)Api("mono_runtime_invoke",typeof(Invoke)))(method,IntPtr.Zero,IntPtr.Zero,out exception);
        if (exception != IntPtr.Zero)
        {
            IntPtr ignored;
            IntPtr message = ((MonoToString)Api("mono_object_to_string",typeof(MonoToString)))(exception,out ignored);
            Console.Error.WriteLine(Marshal.PtrToStringAnsi(((Utf8)Api("mono_string_to_utf8",typeof(Utf8)))(message)));
            return 1;
        }
        return 0;
    }
}
