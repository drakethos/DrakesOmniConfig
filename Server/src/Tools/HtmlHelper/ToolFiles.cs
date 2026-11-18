using System;
using System.IO;
using System.Reflection;
using BepInEx;
using DrakesOmniConfig.Config.Settings;

namespace DrakesOmniConfig.Tools.HtmlHelper;

/// <summary>
/// The Config Builder pages (Fields + Rules) ship inside this DLL as embedded resources and are written to
/// BepInEx/config/DrakesOmniConfig/ConfigBuilder/ at startup, next to GameDataManifest.json and
/// NodeCatalog.json. A file is only rewritten when its content changed, so an open page isn't disturbed.
/// </summary>
internal static class ToolFiles
{
    private const string Prefix = "ConfigBuilder/";

    public static void Extract()
    {
        string root = Path.Combine(Paths.ConfigPath, "DrakesOmniConfig", "ConfigBuilder");
        Assembly asm = typeof(ToolFiles).Assembly;
        int written = 0;
        foreach (string name in asm.GetManifestResourceNames())
        {
            string normalized = name.Replace('\\', '/');
            if (!normalized.StartsWith(Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            string relative = normalized.Substring(Prefix.Length);
            if (relative.Length == 0 || relative.Contains(".."))
            {
                continue;
            }

            try
            {
                using Stream? stream = asm.GetManifestResourceStream(name);
                if (stream == null)
                {
                    continue;
                }

                var bytes = new byte[stream.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int n = stream.Read(bytes, read, bytes.Length - read);
                    if (n <= 0)
                    {
                        break;
                    }

                    read += n;
                }

                string target = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(target) && SameBytes(File.ReadAllBytes(target), bytes))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, bytes);
                written++;
            }
            catch (Exception ex)
            {
                ModLog.Warning($"Could not write Config Builder file '{relative}': {ex.Message}");
            }
        }

        if (written > 0)
        {
            ModLog.Info($"Config Builder: wrote {written} file(s) to '{root}'. Open index.html (fields) or rules.html (node rules) in a browser.");
        }
    }

    private static bool SameBytes(byte[] a, byte[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }
}
