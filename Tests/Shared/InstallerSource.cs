using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CCP.Tests.Shared;

// Resolve only the installer head-selection directives. This is a source contract reader,
// not an ISCC replacement; Windows CI compiles the real scripts for both heads.
internal static class InstallerSource
{
    internal static string Read(string root, string file)
    {
        var defines = new HashSet<string>();
        return Resolve(file);

        string Resolve(string name)
        {
            var result = new StringBuilder();
            var parents = new Stack<bool>();
            bool active = true;
            foreach (var line in File.ReadAllLines(Path.Combine(root, name)))
            {
                var t = line.Trim();
                if (t.StartsWith("#ifdef ") || t.StartsWith("#ifndef "))
                {
                    parents.Push(active);
                    var found = defines.Contains(t.Substring(t.IndexOf(' ') + 1));
                    active = active && (t.StartsWith("#ifndef ") ? !found : found);
                }
                else if (t == "#else") active = parents.Peek() && !active;
                else if (t == "#endif") active = parents.Pop();
                else if (active)
                {
                    if (t == "#include \"installer.iss\"") result.Append(Resolve("installer.iss"));
                    else
                    {
                        if (t.StartsWith("#define ")) defines.Add(t.Substring(8).Split(' ')[0]);
                        // Other includes (content deletions) are irrelevant to identity contracts.
                        else if (t.StartsWith("#") && !t.StartsWith("#include "))
                            throw new InvalidDataException("Unsupported installer directive: " + t);
                        result.AppendLine(line.TrimStart());
                    }
                }
            }
            if (parents.Count != 0) throw new InvalidDataException("Unbalanced installer directives");
            return result.ToString();
        }
    }
}
