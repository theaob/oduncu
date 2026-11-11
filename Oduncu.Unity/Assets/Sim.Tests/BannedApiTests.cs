using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Oduncu.Sim.Tests
{
    /// <summary>
    /// Section 12.2 promises a rule against non-deterministic APIs in the simulation. This test
    /// is the first step towards a Roslyn analyzer: it scans Assets/Sim/**/*.cs, ignoring
    /// comments and string literals, and fails on float, double, System.Random, DateTime,
    /// UnityEngine, or foreach over a Dictionary or HashSet. A line that genuinely needs one of
    /// these (presentation helpers such as FP.AsFloat) carries a "banned-api-ok" comment.
    /// </summary>
    public class BannedApiTests
    {
        private const string AllowMarker = "banned-api-ok";

        private static readonly Regex[] BannedTokens =
        {
            new Regex(@"\bfloat\b"),
            new Regex(@"\bdouble\b"),
            new Regex(@"\bSingle\b"),
            new Regex(@"\bDouble\b"),
            new Regex(@"\bSystem\.Random\b"),
            new Regex(@"(?<![\w.])Random\b"),
            new Regex(@"\bDateTime\b"),
            new Regex(@"\bStopwatch\b"),
            new Regex(@"\bUnityEngine\b"),
        };

        private static readonly Regex UnorderedDeclaration = new Regex(@"\b(?:Dictionary|HashSet)\s*<[^;=(){}]*>\s+(\w+)");
        private static readonly Regex ForeachIn = new Regex(@"\bforeach\s*\([^)]*\bin\s+(?:this\.)?(\w+)");

        [Test]
        public void SimulationSourcesUseNoBannedApis()
        {
            string simDir = FindSimDirectory();
            if (simDir == null)
            {
                Assert.Inconclusive("Could not locate Assets/Sim from the test directory.");
                return;
            }

            var problems = new List<string>();
            string[] files = Directory.GetFiles(simDir, "*.cs", SearchOption.AllDirectories);
            System.Array.Sort(files, System.StringComparer.Ordinal);
            Assert.Greater(files.Length, 5, "expected to find the simulation sources");
            foreach (string file in files)
            {
                foreach (string problem in Scan(File.ReadAllText(file)))
                {
                    problems.Add(Path.GetFileName(file) + ":" + problem);
                }
            }
            Assert.IsEmpty(problems, "Banned APIs in the simulation:\n" + string.Join("\n", problems));
        }

        [Test]
        public void ScannerCatchesEachBannedPattern()
        {
            Assert.IsNotEmpty(Scan("class A { float x; }"));
            Assert.IsNotEmpty(Scan("class A { double x; }"));
            Assert.IsNotEmpty(Scan("class A { System.Random r; }"));
            Assert.IsNotEmpty(Scan("using System; class A { Random r = new Random(); }"));
            Assert.IsNotEmpty(Scan("class A { long t = System.DateTime.Now.Ticks; }"));
            Assert.IsNotEmpty(Scan("using UnityEngine;"));
            Assert.IsNotEmpty(Scan("class A { Dictionary<int, string> _map; void F() { foreach (var kv in _map) { } } }"));
            Assert.IsNotEmpty(Scan("class A { HashSet<int> seen = new HashSet<int>(); void F() { foreach (int i in seen) { } } }"));
            Assert.IsNotEmpty(Scan("class A { Dictionary<int, int> d; void F() { foreach (var k in d.Keys) { } } }"));
        }

        [Test]
        public void ScannerIgnoresCommentsStringsAndAllowedLines()
        {
            Assert.IsEmpty(Scan("// float and double are banned\nclass A { }"));
            Assert.IsEmpty(Scan("/* DateTime */ class A { string s = \"float\"; char c = 'x'; }"));
            Assert.IsEmpty(Scan("class A { DeterministicRandom Rng; }"));
            Assert.IsEmpty(Scan("class A { public float AsFloat => 0; // banned-api-ok: presentation only\n}"));
            Assert.IsEmpty(Scan("class A { List<int> xs; Dictionary<int, int> d; void F() { foreach (int x in xs) { } } }"));
        }

        /// <summary>Returns "line: text" for every banned use in a source file.</summary>
        public static List<string> Scan(string source)
        {
            string code = StripCommentsAndStrings(source);
            string[] codeLines = code.Split('\n');
            string[] rawLines = source.Split('\n');

            var unordered = new HashSet<string>();
            foreach (Match m in UnorderedDeclaration.Matches(code)) unordered.Add(m.Groups[1].Value);

            var problems = new List<string>();
            for (int i = 0; i < codeLines.Length; i++)
            {
                if (rawLines[i].Contains(AllowMarker)) continue;
                string line = codeLines[i];
                foreach (Regex r in BannedTokens)
                {
                    Match m = r.Match(line);
                    if (m.Success) problems.Add((i + 1) + ": " + m.Value + " in `" + rawLines[i].Trim() + "`");
                }
                foreach (Match m in ForeachIn.Matches(line))
                {
                    if (unordered.Contains(m.Groups[1].Value))
                        problems.Add((i + 1) + ": foreach over unordered collection " + m.Groups[1].Value + " in `" + rawLines[i].Trim() + "`");
                }
            }
            return problems;
        }

        /// <summary>Blank out comments and string or char literals, keeping line breaks so line numbers survive.</summary>
        private static string StripCommentsAndStrings(string s)
        {
            var sb = new StringBuilder(s.Length);
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                char next = i + 1 < s.Length ? s[i + 1] : '\0';
                if (c == '/' && next == '/')
                {
                    while (i < s.Length && s[i] != '\n') { sb.Append(' '); i++; }
                }
                else if (c == '/' && next == '*')
                {
                    sb.Append("  ");
                    i += 2;
                    while (i < s.Length && !(s[i] == '*' && i + 1 < s.Length && s[i + 1] == '/'))
                    {
                        sb.Append(s[i] == '\n' ? '\n' : ' ');
                        i++;
                    }
                    if (i < s.Length) { sb.Append("  "); i += 2; }
                }
                else if (c == '"' || c == '\'')
                {
                    bool verbatim = c == '"' && i > 0 && s[i - 1] == '@';
                    char quote = c;
                    sb.Append(' ');
                    i++;
                    while (i < s.Length && s[i] != quote)
                    {
                        if (!verbatim && s[i] == '\\') { sb.Append(' '); i++; }
                        if (i < s.Length) { sb.Append(s[i] == '\n' ? '\n' : ' '); i++; }
                    }
                    if (i < s.Length) { sb.Append(' '); i++; }
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }
            return sb.ToString();
        }

        private static string FindSimDirectory()
        {
            string[] starts = { TestContext.CurrentContext.TestDirectory, Directory.GetCurrentDirectory() };
            foreach (string start in starts)
            {
                string dir = start;
                while (!string.IsNullOrEmpty(dir))
                {
                    string inRepo = Path.Combine(dir, "Oduncu.Unity", "Assets", "Sim");
                    if (Directory.Exists(inRepo)) return inRepo;
                    string inProject = Path.Combine(dir, "Assets", "Sim");
                    if (Directory.Exists(inProject)) return inProject;
                    dir = Path.GetDirectoryName(dir);
                }
            }
            return null;
        }
    }
}
