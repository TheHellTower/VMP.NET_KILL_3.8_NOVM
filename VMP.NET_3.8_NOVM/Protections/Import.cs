using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.PE;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace VMP.NET_3._8_NOVM.Protections
{
    internal static class Import
    {
        private static MethodInfo methodInfo = null;
        internal static void Execute(ModuleDefMD Module)
        {
            methodInfo = Program.Assembly.EntryPoint;
            
            RestoreMethodsModuleVariant(Module);

            DecryptStrings(Module);
            InlineCallMethods(Module);

        }

        private static void InlineCallMethods(ModuleDefMD Module)
        {
            int counterRestoredMetadatas = 0;
            foreach (TypeDef Type in Module.Types.Where(T => T.HasMethods))
            {
                foreach (MethodDef Method in Type.Methods.Where(M => M.HasBody && M.Body.HasInstructions))
                {
                    for (int instructionIndex = 0; instructionIndex < Method.Body.Instructions.Count; instructionIndex++)
                    {
                        if (Method.Body.Instructions[instructionIndex].OpCode == OpCodes.Call)
                        {
                            //Console.WriteLine($"{Method.Name} | {Method.MDToken} | {Method.Body.Instructions[instructionIndex].OpCode} & {Method.Body.Instructions[instructionIndex].Operand} | {instructionIndex}");
                            if (Method.Body.Instructions[instructionIndex].Operand is MethodDef operandCall)
                            {
                                if (operandCall.HasBody && operandCall.Body.Instructions.Count != 1 && operandCall.DeclaringType.Name != methodInfo.DeclaringType.Name)
                                {
                                    try
                                    {
                                        counterRestoredMetadatas++;
                                        var methodCall = operandCall.Body.Instructions[operandCall.Body.Instructions.Count - 2];
                                        Method.Body.Instructions[instructionIndex].OpCode = methodCall.OpCode;
                                        Method.Body.Instructions[instructionIndex].Operand = methodCall.Operand;
                                    }
                                    catch (Exception e)
                                    {
                                        Console.WriteLine(e.ToString());
                                    }
                                }
                            }
                        }
                    }
                }
            }
            Console.WriteLine($"Restored metadatas: {counterRestoredMetadatas}");
        }

        static void RestoreMethodsModuleVariant(ModuleDefMD Module)
        {
            int delegateNumber = 0;
            string declaringTypeName = string.Empty;
            string fieldName = string.Empty;

            foreach(TypeDef Type in Module.Types.Where(T => T.HasMethods/* && T.Name == "Program"*/))
            {
                foreach (MethodDef Method in Type.Methods.Where(M => M.HasBody))
                {
                    try
                    {
                        if (Method.Body.Instructions[0].OpCode == OpCodes.Ldsfld &&
                                Method.Body.Instructions[1].OpCode.ToString().Contains("ldc"))
                        {
                            delegateNumber = Method.Body.Instructions[1].GetLdcI4Value();
                            dynamic ldsfldOperand = Method.Body.Instructions[0]?.Operand;
                            if (ldsfldOperand is FieldDef)
                            {
                                declaringTypeName = ldsfldOperand.DeclaringType.Name;
                                fieldName = ldsfldOperand.Name;
                            }
                            var delegatesArray = (object[])Program.Assembly.ManifestModule.GetType(declaringTypeName)
                                .GetField(fieldName).GetValue(null);
                            var currentDelegate = (Delegate)delegatesArray[delegateNumber];

                            var m_owner = currentDelegate.Method
                                .GetType()
                                .GetField("m_owner", BindingFlags.NonPublic | BindingFlags.Instance)
                                ?.GetValue(currentDelegate.Method);
                            if (m_owner != null)
                            {
                                var m_resolver = m_owner
                                    .GetType()
                                    .GetField("m_resolver", BindingFlags.NonPublic | BindingFlags.Instance)
                                    ?.GetValue(m_owner);
                                if (m_resolver != null)
                                {
                                    var m_scope = m_resolver.GetType()
                                        .GetField("m_scope", BindingFlags.NonPublic | BindingFlags.Instance)
                                        ?.GetValue(m_resolver);
                                    List<object> m_tokens = (List<object>)m_scope.GetType()
                                        .GetField("m_tokens", BindingFlags.NonPublic | BindingFlags.Instance)
                                        .GetValue(m_scope);
                                    if (m_tokens[m_tokens.Count - 1] is RuntimeMethodHandle)
                                    {
                                        RuntimeMethodHandle calledMethod = (RuntimeMethodHandle)m_tokens[m_tokens.Count - 1];
                                        dynamic calledMethodMInfo = calledMethod.GetType()
                                            .GetField("m_value", BindingFlags.NonPublic | BindingFlags.Instance)
                                            ?.GetValue(calledMethod);
                                        if (calledMethodMInfo != null)
                                        {
                                            try
                                            {
                                                string fullName = calledMethodMInfo.GetType().GetProperty("FullName", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(calledMethodMInfo).ToString();

                                                if (fullName.Contains(".ctor") && !fullName.Contains("System.Windows.Forms.Form..ctor"))
                                                {
                                                    Method.Body.Instructions[Method.Body.Instructions.Count - 2] = Instruction.Create(OpCodes.Newobj, Module.Import(calledMethodMInfo));
                                                    CleanMethod(Method);
                                                    Method.Body.UpdateInstructionOffsets();
                                                }
                                                else
                                                {
                                                    Method.Body.Instructions[Method.Body.Instructions.Count - 2] = Instruction.Create(OpCodes.Call, Module.Import(calledMethodMInfo));
                                                    CleanMethod(Method);
                                                    Method.Body.UpdateInstructionOffsets();
                                                }
                                            }
                                            catch (Exception e)
                                            {
                                                Console.WriteLine(e.ToString());
                                            }
                                        }

                                        // * - this runtime Method
                                        Console.WriteLine(delegateNumber + "*: " + calledMethodMInfo.GetType().GetProperty("FullName", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(calledMethodMInfo));
                                    }
                                    else if (m_tokens[m_tokens.Count - 1] is RuntimeFieldHandle)
                                    {
                                        RuntimeFieldHandle calledField = (RuntimeFieldHandle)m_tokens[m_tokens.Count - 1];
                                        dynamic calledFieldFInfo = calledField.GetType()
                                            .GetField("m_ptr", BindingFlags.NonPublic | BindingFlags.Instance)
                                            ?.GetValue(calledField);
                                        if (calledFieldFInfo != null)
                                        {
                                            Method.Body.Instructions[Method.Body.Instructions.Count - 2] = Instruction.Create(OpCodes.Ldsfld, Module.Import(calledFieldFInfo));
                                            CleanMethod(Method);
                                            Method.Body.UpdateInstructionOffsets();
                                        }
                                        // * - this runtime field
                                        Console.WriteLine(delegateNumber + "*: " + calledFieldFInfo.GetType() .GetProperty("FullName", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(calledFieldFInfo));
                                    }
                                    else
                                    {
                                        Console.ForegroundColor = ConsoleColor.Cyan;
                                        Console.WriteLine("UNKNOWN");
                                        Console.ForegroundColor = ConsoleColor.Blue;
                                    }
                                }
                            }
                            else
                            {
                                CleanMethod(Method);
                                Method.Body.Instructions[Method.Body.Instructions.Count - 2] = Instruction.Create(OpCodes.Call, Module.Import(currentDelegate.Method));
                                Console.Write($"==========\n- {Method.Body.Instructions[Method.Body.Instructions.Count - 2]}\n- {currentDelegate.Method}\n==========\n");
                                Console.WriteLine(Method.MDToken);
                                Method.Body.UpdateInstructionOffsets();
                                Console.WriteLine(delegateNumber + ": " + currentDelegate.Method);
                            }

                        }
                    } catch (Exception e)
                    {
                        Console.WriteLine(e.ToString());
                    }
                }
            }
        }

        private static void CleanMethod(MethodDef Method)
        {
            for(var i = 0; i < 3; i++) Method.Body.Instructions.RemoveAt(0);
        }

        static void DecryptStrings(ModuleDefMD Module)
        {
            int counterStrings = 0;
            foreach (var type in Module.Types)
            {
                foreach (var method in type.Methods)
                {
                    if (method.Body == null) continue;
                    for (int instructionIndex = 0; instructionIndex < method.Body.Instructions.Count; instructionIndex++)
                    {
                        if (method.Body.Instructions[instructionIndex].OpCode == OpCodes.Call && method.Body.Instructions[instructionIndex].Operand is MethodDef)
                        {
                            var callOperand = (MethodDef)method.Body.Instructions[instructionIndex].Operand;
                            if (callOperand.ReturnType.TypeName == "String" && callOperand.IsStatic)
                            {
                                if (callOperand.Body.Instructions[0].OpCode == OpCodes.Newobj &&
                                    callOperand.Body.Instructions[8].IsLdcI4() &&
                                    callOperand.Body.Instructions[9].OpCode == OpCodes.Call &&
                                    (callOperand.Body.Instructions[10].OpCode == OpCodes.Castclass && callOperand.Body.Instructions[10].Operand.ToString().Contains("System.String")) &&
                                    callOperand.Body.Instructions[11].OpCode == OpCodes.Ret)
                                {
                                    counterStrings++;
                                    method.Body.Instructions[instructionIndex].OpCode = OpCodes.Ldstr;
                                    method.Body.Instructions[instructionIndex].Operand = InvokeDecryptMethod(callOperand.Module.Name, callOperand.DeclaringType2.Name,callOperand.Name.String, Convert.ToUInt32(method.Body.Instructions[instructionIndex - 1].GetLdcI4Value()));
                                    method.Body.Instructions.RemoveAt(instructionIndex - 1);
                                }
                            }
                        }
                    }
                    method.Body.UpdateInstructionOffsets();
                }
            }
            Console.WriteLine($"Decrypted strings: {counterStrings}");
        }
        static string InvokeDecryptMethod(string moduleName, string typeName, string methodName, uint value)
        {
            string decryptedString;
            var module = methodInfo.Module.Assembly.GetModule(moduleName);
            foreach (var type in module.GetTypes())
            {
                if (type.Name == typeName)
                {
                    foreach (var methodType in type.GetRuntimeMethods())
                    {
                        if (methodType.Name == methodName)
                        {
                            decryptedString = (string)methodType.Invoke(null, new object[] { value });
                            return decryptedString;
                        }
                    }
                }
            }
            return "NULL";
        }
    }
}
