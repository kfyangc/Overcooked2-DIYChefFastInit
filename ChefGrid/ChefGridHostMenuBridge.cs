using System;
using System.Reflection;
using Team17.Online;

namespace OC2DIYChef
{
    // Optional registration through the installed HostUtilities menu service.
    // No assembly reference or modification of HostUtilities.dll is required.
    internal static class ChefGridHostMenuBridge
    {
        internal const string Key = "DIYChef_ChangeChef";

        internal static bool Register(Type service, Func<User, bool> visible,
            Action<UIPlayerMenuBehaviour> click, Func<User, bool> enabled)
        {
            if (service == null) return false;
            var register = service.GetMethod("Register", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(string), typeof(Func<User, bool>), typeof(Action<UIPlayerMenuBehaviour>), typeof(Func<User, bool>) }, null);
            if (register == null) return false;
            register.Invoke(null, new object[] { Key, visible, click, enabled });
            return true;
        }
    }
}
