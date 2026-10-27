// ============================================================================
// 模块名称: CsScriptDriverLoader.cs
// 所属区域: Hardware (动态脚本扩展包内存编译引擎层)
// 核心职责:
//   使用 .NET Framework 4.0 原生 Microsoft.CSharp.CSharpCodeProvider 在纯内存
//   (GenerateInMemory = true) 中动态编译 adapters/*.fp-driver.cs C# 5.0 硬件扩展包，
//   自动实例化并挂载 IHardwareDriver 实现类 (Priority = 300)，零磁盘临时文件污染，
//   并在脚本存在语法错误时进行容灾隔离与友好诊断。
// 依赖关系: IHardwareDriver.cs, HardwareDriverBase.cs, HardwareDriverRegistry.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止 $"..." 插值、禁止 ?. 空条件运算符、禁止 nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.CSharp;

namespace RazerBatteryTray
{
    public static class CsScriptDriverLoader
    {
        private static readonly object compileLock = new object();

        static CsScriptDriverLoader()
        {
            try
            {
                AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
            }
            catch { }
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                Assembly selfAsm = typeof(IHardwareDriver).Assembly;
                if (selfAsm != null && args != null && !string.IsNullOrEmpty(args.Name))
                {
                    string selfName = selfAsm.GetName().Name;
                    if (args.Name.Equals(selfAsm.FullName, StringComparison.OrdinalIgnoreCase) ||
                        args.Name.StartsWith(selfName + ",", StringComparison.OrdinalIgnoreCase) ||
                        args.Name.Equals(selfName, StringComparison.OrdinalIgnoreCase))
                    {
                        return selfAsm;
                    }
                }
            }
            catch { }
            return null;
        }

        private static string ResolveSelfAssemblyPath()
        {
            Assembly selfAsm = typeof(IHardwareDriver).Assembly;
            string selfLoc = "";
            try { selfLoc = selfAsm.Location; } catch { }
            if (!string.IsNullOrEmpty(selfLoc) && File.Exists(selfLoc)) return selfLoc;

            try
            {
                string c1 = Path.Combine(Environment.CurrentDirectory, "FerrisPulse.exe");
                if (File.Exists(c1)) return Path.GetFullPath(c1);
            }
            catch { }

            try
            {
                string c2 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FerrisPulse.exe");
                if (File.Exists(c2)) return Path.GetFullPath(c2);
            }
            catch { }

            return "";
        }

        public static List<IHardwareDriver> CompileDriversFromSource(string csSource, string virtualFilePath, out List<string> errors)
        {
            errors = new List<string>();
            List<IHardwareDriver> drivers = new List<IHardwareDriver>();

            if (string.IsNullOrEmpty(csSource) || csSource.Trim().Length == 0)
            {
                errors.Add("脚本内容为空。");
                return drivers;
            }

            lock (compileLock)
            {
                string tempRefCopy = "";
                try
                {
                    using (CSharpCodeProvider provider = new CSharpCodeProvider())
                    {
                        CompilerParameters cp = new CompilerParameters();
                        cp.GenerateInMemory = true;
                        cp.GenerateExecutable = false;
                        cp.IncludeDebugInformation = false;
                        cp.TreatWarningsAsErrors = false;

                        cp.ReferencedAssemblies.Add("mscorlib.dll");
                        cp.ReferencedAssemblies.Add("System.dll");
                        cp.ReferencedAssemblies.Add("System.Core.dll");
                        cp.ReferencedAssemblies.Add("System.Drawing.dll");
                        cp.ReferencedAssemblies.Add("System.Windows.Forms.dll");

                        string selfLoc = ResolveSelfAssemblyPath();
                        if (!string.IsNullOrEmpty(selfLoc) && File.Exists(selfLoc))
                        {
                            // 若路径包含特殊非 ASCII 字符，拷贝至纯 ASCII 临时路径供 csc.exe 引用，完成后立即删除，零句柄占用
                            bool hasNonAscii = false;
                            for (int si = 0; si < selfLoc.Length; si++)
                            {
                                if (selfLoc[si] > 127) { hasNonAscii = true; break; }
                            }
                            if (hasNonAscii)
                            {
                                tempRefCopy = Path.Combine(Path.GetTempPath(), "fp_ref_" + Guid.NewGuid().ToString("N") + ".exe");
                                File.Copy(selfLoc, tempRefCopy, true);
                                cp.ReferencedAssemblies.Add(tempRefCopy);
                            }
                            else
                            {
                                cp.ReferencedAssemblies.Add(selfLoc);
                            }
                        }

                        CompilerResults cr = provider.CompileAssemblyFromSource(cp, csSource);
                        if (cr.Errors != null && cr.Errors.HasErrors)
                        {
                            for (int i = 0; i < cr.Errors.Count; i++)
                            {
                                CompilerError err = cr.Errors[i];
                                if (err != null && !err.IsWarning)
                                {
                                    errors.Add(string.Format("[{0} 行 {1}] {2}: {3}",
                                        Path.GetFileName(virtualFilePath ?? "script.fp-driver.cs"),
                                        err.Line,
                                        err.ErrorNumber,
                                        err.ErrorText));
                                }
                            }
                            return drivers;
                        }

                        Assembly compiledAsm = cr.CompiledAssembly;
                        if (compiledAsm != null)
                        {
                            Type[] types = compiledAsm.GetTypes();
                            for (int i = 0; i < types.Length; i++)
                            {
                                Type t = types[i];
                                if (t != null && t.IsClass && !t.IsAbstract && typeof(IHardwareDriver).IsAssignableFrom(t))
                                {
                                    try
                                    {
                                        IHardwareDriver instance = Activator.CreateInstance(t) as IHardwareDriver;
                                        if (instance != null)
                                        {
                                            HardwareDriverBase baseDrv = instance as HardwareDriverBase;
                                            if (baseDrv != null && !string.IsNullOrEmpty(virtualFilePath))
                                            {
                                                baseDrv.SourceFilePath = virtualFilePath;
                                            }
                                            drivers.Add(instance);
                                        }
                                    }
                                    catch (Exception exInst)
                                    {
                                        errors.Add("实例化驱动类 " + t.FullName + " 失败: " + exInst.Message);
                                    }
                                }
                            }
                        }

                        if (drivers.Count == 0 && errors.Count == 0)
                        {
                            errors.Add("脚本编译成功，但未发现实现 IHardwareDriver 或继承 HardwareDriverBase 的公开非抽象类。");
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add("脚本编译器异常: " + ex.Message);
                }
                finally
                {
                    if (!string.IsNullOrEmpty(tempRefCopy))
                    {
                        try { if (File.Exists(tempRefCopy)) File.Delete(tempRefCopy); } catch { }
                    }
                }
            }

            return drivers;
        }

        public static IHardwareDriver ValidateAndCompileScript(string csSource, out List<string> errors)
        {
            List<IHardwareDriver> list = CompileDriversFromSource(csSource, "preview.fp-driver.cs", out errors);
            if (list != null && list.Count > 0) return list[0];
            return null;
        }

        public static string SaveScriptDriverToDisk(string csSource, IHardwareDriver driver)
        {
            string dir = FdasRuntimeHelper.GetAdaptersDirectory();
            if (string.IsNullOrEmpty(dir))
            {
                dir = FdasRuntimeHelper.GetAppDataAdaptersDirectory();
            }
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string rawId = (driver != null && !string.IsNullOrEmpty(driver.DriverId)) ? driver.DriverId : "custom_driver";
            string safeId = rawId.Replace(":", "_").Replace("/", "_").Replace("\\", "_").Replace(" ", "_").Replace(".", "_");
            string fullPath = Path.Combine(dir, safeId + ".fp-driver.cs");

            UTF8Encoding utf8Bom = new UTF8Encoding(true);
            File.WriteAllText(fullPath, csSource ?? "", utf8Bom);
            return fullPath;
        }

        public static List<IHardwareDriver> LoadAndRegisterScriptDrivers()
        {
            List<string> dummy;
            return LoadAndRegisterScriptDrivers(out dummy);
        }

        public static List<IHardwareDriver> LoadAndRegisterScriptDrivers(out List<string> allErrors)
        {
            allErrors = new List<string>();
            List<IHardwareDriver> loaded = new List<IHardwareDriver>();
            List<string> dirs = new List<string>();

            string d1 = FdasRuntimeHelper.GetAdaptersDirectory();
            if (!string.IsNullOrEmpty(d1) && Directory.Exists(d1)) dirs.Add(d1);

            string d2 = FdasRuntimeHelper.GetAppDataAdaptersDirectory();
            if (!string.IsNullOrEmpty(d2) && Directory.Exists(d2) && !dirs.Contains(d2)) dirs.Add(d2);

            for (int di = 0; di < dirs.Count; di++)
            {
                string dir = dirs[di];
                try
                {
                    string[] csFiles = Directory.GetFiles(dir, "*.fp-driver.cs");
                    if (csFiles == null) continue;
                    for (int fi = 0; fi < csFiles.Length; fi++)
                    {
                        string file = csFiles[fi];
                        try
                        {
                            string src = File.ReadAllText(file, Encoding.UTF8);
                            List<string> errs;
                            List<IHardwareDriver> list = CompileDriversFromSource(src, file, out errs);
                            if (errs != null && errs.Count > 0)
                            {
                                allErrors.AddRange(errs);
                            }
                            if (list != null)
                            {
                                for (int k = 0; k < list.Count; k++)
                                {
                                    HardwareDriverRegistry.RegisterDriver(list[k]);
                                    loaded.Add(list[k]);
                                }
                            }
                        }
                        catch (Exception exFile)
                        {
                            allErrors.Add("读取脚本文件失败 (" + Path.GetFileName(file) + "): " + exFile.Message);
                        }
                    }
                }
                catch { }
            }

            return loaded;
        }
    }
}
