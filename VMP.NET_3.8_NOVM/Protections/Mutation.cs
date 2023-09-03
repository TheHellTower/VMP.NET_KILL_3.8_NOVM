using de4dot.blocks;
using de4dot.blocks.cflow;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using System.Collections.Generic;
using System.Linq;

namespace VMP.NET_3._8_NOVM.Protections
{
    internal static class Mutation
    {
        internal static void Execute(ModuleDefMD Module)
        {
            foreach (TypeDef Type in Module.Types)
                foreach (MethodDef Method in Type.Methods.Where(M => M.HasBody && M.Body.HasInstructions))
                {
                    BlocksCflowDeobfuscator deobfuscator = new BlocksCflowDeobfuscator();
                    deobfuscator.Add(new ControlFlowDeobfuscator());
                    deobfuscator.Add(new MethodCallInliner(true));
                    
                    Blocks blocks = new Blocks(Method);
                    deobfuscator.Initialize(blocks);
                    deobfuscator.Deobfuscate();
                    blocks.RemoveDeadBlocks();
                    blocks.RepartitionBlocks();
                    blocks.UpdateBlocks();
                    deobfuscator.Deobfuscate();
                    blocks.RepartitionBlocks();
                    
                    IList<Instruction> allInstructions;
                    IList<ExceptionHandler> allExceptionHandlers;
                    
                    blocks.GetCode(out allInstructions, out allExceptionHandlers);
                    DotNetUtils.RestoreBody(Method, allInstructions, allExceptionHandlers);
                }
        }

        public class ControlFlowDeobfuscator : IBlocksDeobfuscator
        {
            public static Code[] ArithmeticOpCodes;

            private static InstructionEmulator Emulator;

            private Local Context;

            private MethodDef Method;

            private List<Block> VisitedBlocks;

            private static readonly Code[] ArithmethicCodes;

            public bool ExecuteIfNotModified => false;

            public void DeobfuscateBegin(Blocks blocks)
            {
                MethodDef method = (Method = blocks.Method);
                Emulator.Initialize(method);
                Context = FindContext(method);
                VisitedBlocks = new List<Block>();
            }

            public bool Deobfuscate(List<Block> allBlocks)
            {
                if (Context == null)
                    return false;

                foreach (Block block in allBlocks)
                    ProcessBlock(block);

                return false;
            }

            private void ProcessBlock(Block block, Value value = null)
            {
                if (VisitedBlocks.Contains(block))
                    return;

                VisitedBlocks.Add(block);
                if (value != null)
                    Emulator.SetLocal(Context, value);

                foreach (Instr instr in block.Instructions)
                    ProcessInstruction(instr.Instruction);

                foreach (Block target in block.GetTargets())
                    ProcessBlock(target, Emulator.GetLocal(Context));
            }

            private void ProcessInstruction(Instruction instruction)
            {
                if (instruction.IsStloc())
                {
                    Local local = instruction.GetLocal(Method.Body.Variables);
                    if (local == Context)
                    {
                        Emulator.Emulate(instruction);
                        return;
                    }
                    Emulator.Pop();
                    Emulator.MakeLocalUnknown(local);
                }
                else if (instruction.IsLdloc())
                {
                    Emulator.Emulate(instruction);
                    Local local2 = instruction.GetLocal(Method.Body.Variables);
                    if (local2 == Context)
                    {
                        instruction.OpCode = OpCodes.Ldc_I4;
                        instruction.Operand = (Emulator.Peek() as Int32Value).Value;
                    }
                }
                else
                {
                    Emulator.Emulate(instruction);
                }
            }

            private Local FindContext(MethodDef method)
            {
                Dictionary<Local, int> LocalFlows = new Dictionary<Local, int>();
                LocalList locals = method.Body.Variables;
                foreach (Local local in locals)
                    if (local.Type.ElementType == ElementType.U4)
                        LocalFlows.Add(local, 0);

                IList<Instruction> instructions = method.Body.Instructions;
                for (int i = 0; instructions.Count > i; i++)
                {
                    Instruction instr = instructions[i];
                    if (instr.OpCode == OpCodes.Ldloca || instr.OpCode == OpCodes.Ldloca_S)
                    {
                        Local local2 = instr.GetLocal(locals);
                        if (LocalFlows.ContainsKey(local2))
                            LocalFlows.Remove(local2);
                    }
                    else
                    {
                        if (!instr.IsStloc())
                            continue;
                        Local local3 = instr.GetLocal(locals);
                        if (LocalFlows.ContainsKey(local3))
                        {
                            Instruction before = instructions[i - 1];
                            if (!before.IsLdcI4() && !IsArithmethic(before))
                                LocalFlows.Remove(local3);
                            else
                                LocalFlows[local3]++;
                        }
                    }
                }
                if (LocalFlows.Count == 1)
                    return LocalFlows.Keys.ToArray()[0];
                
                if (LocalFlows.Count == 0)
                    return null;

                int highestCount = 0;
                Local highestLocal = null;
                foreach (KeyValuePair<Local, int> entry in LocalFlows)
                {
                    if (entry.Value > highestCount)
                    {
                        highestLocal = entry.Key;
                        highestCount = entry.Value;
                    }
                }
                return highestLocal;
            }

            private bool IsArithmethic(Instruction instruction) => ArithmethicCodes.Contains(instruction.OpCode.Code);

            static ControlFlowDeobfuscator()
            {
                ArithmeticOpCodes = new Code[17]
                {
                    Code.Add,
                    Code.Shr,
                    Code.Mul,
                    Code.Div,
                    Code.Rem,
                    Code.Or,
                    Code.And,
                    Code.Not,
                    Code.Shl,
                    Code.Xor,
                    Code.Shr_Un,
                    Code.Add_Ovf_Un,
                    Code.Div_Un,
                    Code.Mul_Ovf_Un,
                    Code.Sub_Ovf_Un,
                    Code.Sub,
                    Code.Rem_Un
                };
                Emulator = new InstructionEmulator();
                ArithmethicCodes = ArithmeticOpCodes;
            }
        }
    }
}