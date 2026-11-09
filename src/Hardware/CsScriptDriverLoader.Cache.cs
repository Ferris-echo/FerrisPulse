// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// C# 5 / .NET 4. Retain unchanged script instances and revoke removed/invalid files.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RazerBatteryTray
{
    public static partial class CsScriptDriverLoader
    {
        private sealed class ScriptCacheEntry
        {
            internal string Source;
            internal List<IHardwareDriver> Drivers;
            internal List<string> Errors;
        }

        private static readonly object loadLock = new object();
        private static readonly Dictionary<string, ScriptCacheEntry> scriptCache =
            new Dictionary<string, ScriptCacheEntry>(StringComparer.OrdinalIgnoreCase);

        private static void RevokeScriptEntry(ScriptCacheEntry entry)
        {
            if (entry == null || entry.Drivers == null) return;
            foreach (IHardwareDriver previous in entry.Drivers)
            {
                // Atomic identity check: a later file/JSON replacement with this ID remains intact.
                HardwareDriverRegistry.UnregisterDriverInstance(previous);
            }
        }

        private static List<IHardwareDriver> LoadAndRegisterScriptFiles(string[] files, out List<string> allErrors)
        {
            lock (loadLock)
            {
                allErrors = new List<string>();
                List<IHardwareDriver> loaded = new List<IHardwareDriver>();
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string candidate in files ?? new string[0])
                {
                    string file = Path.GetFullPath(candidate);
                    if (!seen.Add(file)) continue;
                    ScriptCacheEntry entry;
                    scriptCache.TryGetValue(file, out entry);
                    try
                    {
                        string source = File.ReadAllText(file, Encoding.UTF8);
                        if (entry == null || !string.Equals(entry.Source, source, StringComparison.Ordinal))
                        {
                            RevokeScriptEntry(entry);
                            List<string> errors;
                            List<IHardwareDriver> compiled = CompileDriversFromSource(source, file, out errors);
                            entry = new ScriptCacheEntry { Source = source, Drivers = compiled, Errors = errors };
                            scriptCache[file] = entry;
                        }
                        allErrors.AddRange(entry.Errors);
                        foreach (IHardwareDriver driver in entry.Drivers)
                        {
                            HardwareDriverRegistry.RegisterDriver(driver);
                            loaded.Add(driver);
                        }
                    }
                    catch (Exception ex)
                    {
                        RevokeScriptEntry(entry);
                        scriptCache.Remove(file);
                        allErrors.Add("读取脚本文件失败 (" + Path.GetFileName(file) + "): " + ex.Message);
                    }
                }
                foreach (string oldFile in new List<string>(scriptCache.Keys))
                {
                    if (seen.Contains(oldFile)) continue;
                    RevokeScriptEntry(scriptCache[oldFile]);
                    scriptCache.Remove(oldFile);
                }
                return loaded;
            }
        }
    }
}
