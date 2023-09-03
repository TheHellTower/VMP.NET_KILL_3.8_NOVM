using dnlib.DotNet;
using dnlib.PE;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace VMP.NET_3._8_NOVM.Protections
{
    internal static class AntiTamper
    {
        internal static void Execute(ModuleDefMD Module)
        {
            //Add Anti-Tamper Detection, so if not detected, do nothing.
        }
    }
}
