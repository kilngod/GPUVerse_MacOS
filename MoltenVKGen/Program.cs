﻿using System.CodeDom.Compiler;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using MoltenVKGen;
#nullable disable

class Program
{
    static void Main(string[] args)
    {
        string outputPath = "..\\..\\..\\..\\MacCatalystVulkan\\VulkanGenerated";

        GenerateFiles(outputPath);

        outputPath = "..\\..\\..\\..\\MacVulkan\\VulkanGenerated";

    }

    public static void GenerateFiles(string outputPath)
    {
        string vkFile = "..\\net7.0\\KhronosRegistry\\vk.xml";
      

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            vkFile = vkFile.Replace("\\", "/");
            outputPath = outputPath.Replace("\\", "/");
        }

        var vulkanSpec = VulkanSpecification.FromFile(vkFile);

        var vulkanVersion = VulkanVersion.FromSpec(vulkanSpec, "AllVersions", vulkanSpec.Extensions.ToImmutableList());

        // Write Constants
        using (StreamWriter file = File.CreateText(Path.Combine(outputPath, "Constants.cs")))
        {
            file.WriteLine("#nullable disable\n");
            file.WriteLine("namespace GPUVulkan;");

            //    file.WriteLine("{");
            file.WriteLine("\tpublic static partial class VulkanNative");
            file.WriteLine("\t{");

            foreach (var constant in vulkanVersion.Constants)
            {
                if (constant.Alias != null)
                {
                    var refConstant = vulkanVersion.Constants.FirstOrDefault(c => c.Name == constant.Alias);
                    file.WriteLine(
                        $"\t\tpublic const {refConstant.Type.ToCSharp()} {constant.Name} = {refConstant.Name};");
                }
                else
                {
                    file.WriteLine(
                        $"\t\tpublic const {constant.Type.ToCSharp()} {constant.Name} = {ConstantDefinition.NormalizeValue(constant.Value)};");
                }
            }

            file.WriteLine("\t}");
            //   file.WriteLine("}");
        }

        // Delegates
        using (StreamWriter file = File.CreateText(Path.Combine(outputPath, "Delegates.cs")))
        {
            file.WriteLine("using System;\n");
            file.WriteLine("#nullable disable\n");
            file.WriteLine("namespace GPUVulkan;");
            // file.WriteLine("{");

            foreach (var func in vulkanVersion.FuncPointers)
            {
                file.Write($"\tpublic unsafe delegate {func.Type} {func.Name}(");
                if (func.Parameters.Count > 0)
                {
                    file.Write("\n");
                    string type, convertedType;

                    for (int p = 0; p < func.Parameters.Count; p++)
                    {
                        if (p > 0)
                            file.Write(",\n");

                        type = func.Parameters[p].Type;
                        var typeDef = vulkanSpec.TypeDefs.Find(t => t.Name == type);
                        if (typeDef != null)
                        {
                            vulkanSpec.BaseTypes.TryGetValue(typeDef.Type, out type);
                        }

                        convertedType = Helpers.ConvertBasicTypes(type);
                        if (convertedType == string.Empty)
                        {
                            convertedType = type;
                        }

                        file.Write(
                            $"\t\t{Helpers.GetPrettyEnumName(convertedType)} {Helpers.ValidatedName(func.Parameters[p].Name)}");
                    }
                }

                file.Write(");\n\n");
            }

            //     file.WriteLine("}");
        }

        // Enums
        using (StreamWriter file = File.CreateText(Path.Combine(outputPath, "Enums.cs")))
        {
            file.WriteLine("using System;\n");
            file.WriteLine("#nullable disable\n");
            file.WriteLine("namespace GPUVulkan;");
            //  file.WriteLine("{");

            foreach (var e in vulkanVersion.Enums)
            {
                if (e.Type == EnumType.Bitmask)
                    file.WriteLine("\t[Flags]");

                file.WriteLine($"\tpublic enum {Helpers.GetPrettyEnumName(e.Name)}");
                file.WriteLine("\t{");

                if (!(e.Values.Exists(v => v.Value == 0)))
                {
                    file.WriteLine("\t\tNone = 0,");
                }

                foreach (var member in e.Values)
                {
                    file.WriteLine($"\t\t{member.Name} = {member.Value},");
                }

                file.WriteLine("\t}\n");

            }

            // file.WriteLine("}");
        }

        // Unions
        using (StreamWriter file = File.CreateText(Path.Combine(outputPath, "Unions.cs")))
        {
            file.WriteLine("using System.Runtime.InteropServices;\n");
            file.WriteLine("#nullable disable\n");
            file.WriteLine("namespace GPUVulkan;");
            //    file.WriteLine("{");

            foreach (var union in vulkanVersion.Unions)
            {
                file.WriteLine("\t[StructLayout(LayoutKind.Explicit)]");
                file.WriteLine($"\tpublic unsafe partial struct {union.Name}");
                file.WriteLine("\t{");
                foreach (var member in union.Members)
                {
                    string csType = Helpers.ConvertToCSharpType(member.Type, member.PointerLevel, vulkanSpec);

                    file.WriteLine($"\t\t[FieldOffset(0)]");
                    if (member.ElementCount > 1)
                    {
                        file.WriteLine($"\t\tpublic unsafe fixed {csType} {member.Name}[{member.ElementCount}];");
                    }
                    else
                    {
                        file.WriteLine($"\t\tpublic {csType} {member.Name};");
                    }
                }

                file.WriteLine("\t}\n");
            }

            //   file.WriteLine("}\n");
        }

        // structs
        using (StreamWriter file = File.CreateText(Path.Combine(outputPath, "Structs.cs")))
        {
            file.WriteLine("using System;");
            file.WriteLine("using System.Runtime.InteropServices;\n");
            file.WriteLine("#nullable disable\n");
            file.WriteLine("namespace GPUVulkan;");
            // file.WriteLine("{");

            foreach (var structure in vulkanVersion.Structs)
            {
                var useExplicitLayout = structure.Members.Any(s => s.ExplicityLayoutValue.HasValue == true);
                int layoutValue = 0;
                if (useExplicitLayout)
                {
                    file.WriteLine("\t[StructLayout(LayoutKind.Explicit)]");
                }
                else
                {
                    file.WriteLine("\t[StructLayout(LayoutKind.Sequential)]");
                }

                file.WriteLine($"\tpublic unsafe partial struct {structure.Name}");
                file.WriteLine("\t{");


                foreach (var member in structure.Members)
                {
                    // Avoid duplicate members from Vulkan Safety Critical
                    if (Helpers.IsVKSC(member.Api))
                    {
                        continue;
                    }

                    if (useExplicitLayout)
                    {
                        file.WriteLine($"\t\t[FieldOffset({layoutValue})]");
                        layoutValue += Member.GetSizeInBytes(member, vulkanVersion);
                    }

                    string csType =
                        Helpers.GetPrettyEnumName(Helpers.ConvertToCSharpType(member.Type, member.PointerLevel,
                            vulkanSpec));

                    if (member.ElementCount > 1)
                    {
                        for (int i = 0; i < member.ElementCount; i++)
                        {
                            file.WriteLine($"\t\tpublic {csType} {member.Name}_{i};");
                        }
                    }
                    else if (member.ConstantValue != null)
                    {
                        var validConstant = vulkanVersion.Constants.FirstOrDefault(c => c.Name == member.ConstantValue);

                        if (Helpers.SupportFixed(csType))
                        {
                            file.WriteLine(
                                $"\t\tpublic fixed {csType} {Helpers.ValidatedName(member.Name)}[(int)VulkanNative.{validConstant.Name}];");
                        }
                        else
                        {
                            int count = 0;

                            if (validConstant.Value == null)
                            {
                                var alias = vulkanVersion.Constants.FirstOrDefault(c => c.Name == validConstant.Alias);
                                count = int.Parse(alias.Value);
                            }
                            else
                            {
                                count = int.Parse(validConstant.Value);
                            }

                            for (int i = 0; i < count; i++)
                            {
                                file.WriteLine($"\t\tpublic {csType} {member.Name}_{i};");
                            }
                        }
                    }
                    else
                    {
                        file.WriteLine($"\t\tpublic {csType} {Helpers.ValidatedName(member.Name)};");
                    }
                }

                file.WriteLine("\t}\n");
            }

            // file.WriteLine("}\n");
        }

        // Handles
        using (StreamWriter file = File.CreateText(Path.Combine(outputPath, "Handles.cs")))
        {
            file.WriteLine("using System;\n");
            file.WriteLine("#nullable disable\n");
            file.WriteLine("namespace GPUVulkan;");
            //  file.WriteLine("{");

            foreach (var handle in vulkanVersion.Handles)
            {
                file.WriteLine($"\tpublic partial struct {handle.Name} : IEquatable<{handle.Name}>");
                file.WriteLine("\t{");
                string handleType = handle.Dispatchable ? "IntPtr" : "ulong";
                string nullValue = handle.Dispatchable ? "IntPtr.Zero" : "0";

                file.WriteLine($"\t\tpublic readonly {handleType} Handle;");

                file.WriteLine($"\t\tpublic {handle.Name}({handleType} existingHandle) {{ Handle = existingHandle; }}");
                file.WriteLine($"\t\tpublic static {handle.Name} Null => new {handle.Name}({nullValue});");
                file.WriteLine(
                    $"\t\tpublic static implicit operator {handle.Name}({handleType} handle) => new {handle.Name}(handle);");
                file.WriteLine(
                    $"\t\tpublic static bool operator ==({handle.Name} left, {handle.Name} right) => left.Handle == right.Handle;");
                file.WriteLine(
                    $"\t\tpublic static bool operator !=({handle.Name} left, {handle.Name} right) => left.Handle != right.Handle;");
                file.WriteLine(
                    $"\t\tpublic static bool operator ==({handle.Name} left, {handleType} right) => left.Handle == right;");
                file.WriteLine(
                    $"\t\tpublic static bool operator !=({handle.Name} left, {handleType} right) => left.Handle != right;");
                file.WriteLine($"\t\tpublic bool Equals({handle.Name} h) => Handle == h.Handle;");
                file.WriteLine($"\t\tpublic override bool Equals(object o) => o is {handle.Name} h && Equals(h);");
                file.WriteLine($"\t\tpublic override int GetHashCode() => Handle.GetHashCode();");
                file.WriteLine("\t}\n");
            }

            // file.WriteLine("}");
        }

        // Commands
        using (StreamWriter file = File.CreateText(Path.Combine(outputPath, "Commands.cs")))
        {
            file.WriteLine("using System;");
            file.WriteLine("using System.Runtime.InteropServices;\n");
            file.WriteLine("#nullable disable\n");
            file.WriteLine("namespace GPUVulkan;");
            //file.WriteLine("{");
            file.WriteLine("\tpublic static unsafe partial class VulkanNative");
            file.WriteLine("\t{");

            foreach (var command in vulkanVersion.Commands)
            {
                string convertedType = Helpers.ConvertToCSharpType(command.Prototype.Type, 0, vulkanSpec);

                file.WriteLine("\t\t[DllImport (\"__Internal\")]");

                /*    // Delegate
                    file.WriteLine(
                        $"\t\tprivate delegate {convertedType} {command.Prototype.Name}Delegate({command.GetParametersSignature(vulkanSpec)});");

                    // internal function
                    file.WriteLine($"\t\tprivate static {command.Prototype.Name}Delegate {command.Prototype.Name}_ptr;");
    */
                // public function
                file.WriteLine(
                    $"\t\tpublic static extern {convertedType} {command.Prototype.Name}({command.GetParametersSignature(vulkanSpec)});\n\n");
                //file.WriteLine($"\t\t\t=> {command.Prototype.Name}_ptr({command.GetParametersSignature(vulkanSpec, useTypes: false)});\n");
            }

            /*
            file.WriteLine($"\t\tpublic static void LoadFuncionPointers(VkInstance instance = default)");
            file.WriteLine("\t\t{");
            file.WriteLine("\t\t\tif (instance != default)");
            file.WriteLine("\t\t\t{");
            file.WriteLine("\t\t\t\tNativeLib.instance = instance;");
            file.WriteLine("\t\t\t}");
            file.WriteLine();

            foreach (var command in vulkanVersion.Commands)
            {
                file.WriteLine(
                    $"\t\t\tNativeLib.LoadFunction(\"{command.Prototype.Name}\",  out {command.Prototype.Name}_ptr);");
            }
            */

            file.WriteLine("\t\t}");
            // file.WriteLine("\t}");
            //file.WriteLine("}");
        }

    }
}
