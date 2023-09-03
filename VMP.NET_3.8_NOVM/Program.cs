using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.MD;
using dnlib.DotNet.Writer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace VMP.NET_3._8_NOVM
{
    internal class Program
    {
        static ModuleDefMD Module = null;
        public static Assembly Assembly = null;
        static void Main(string[] args)
        {
            Console.Clear();
            Module = ModuleDefMD.Load(args[0]);
            Assembly = Assembly.LoadFile(Path.GetFullPath(args[0]));

            if (HasVMP())
            {
                //Protections.AntiTamper.Execute(Module);
                
                Protections.Mutation.Execute(Module);
                Protections.Import.Execute(Module);
            }
            var nativeModuleWriter = new NativeModuleWriterOptions(Module, false);
            nativeModuleWriter.Logger = DummyLogger.NoThrowInstance;
            nativeModuleWriter.MetadataOptions.Flags = MetadataFlags.PreserveAll |
                                                       MetadataFlags.KeepOldMaxStack |
                                                       MetadataFlags.PreserveExtraSignatureData |
                                                       MetadataFlags.PreserveBlobOffsets |
                                                       MetadataFlags.PreserveUSOffsets |
                                                       MetadataFlags.PreserveStringsOffsets;
            nativeModuleWriter.Cor20HeaderOptions.Flags = ComImageFlags.ILOnly;


            Module.NativeWrite(Module.Location.Insert(Module.Location.Length - 4, "-VMPed"), nativeModuleWriter);
        }

        private static bool HasVMP()
        {
            List<Instruction> GTCI = Module.GlobalType.FindOrCreateStaticConstructor().Body.Instructions.ToList();
            return GTCI.Count() == 6 && GTCI[0].OpCode == OpCodes.Newobj && GTCI[3].OpCode == OpCodes.Call;
        }
    }
}
