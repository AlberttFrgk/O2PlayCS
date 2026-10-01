using System;
using System.IO;

namespace O2Play
{
    // Represents parsed command-line arguments.
    public class LaunchArguments
    {
        public bool IsStop { get; set; }
        public bool IsPlay { get; set; }
        public int Measure { get; set; }
        public string? FilePath { get; set; }
        public OjnDifficulty? Difficulty { get; set; }
    }

    // Parses legacy o2play flags (-P, -N[measure], -S, -EX/-NX/-HX).
    public static class CommandLineParser
    {
        public static LaunchArguments Parse(string[] args)
        {
            var result = new LaunchArguments();

            for (int i = 0; i < args.Length; i++)
            {
                string raw = args[i].Trim();
                if (raw.StartsWith("\"") && raw.EndsWith("\"") && raw.Length >= 2)
                {
                    raw = raw.Substring(1, raw.Length - 2).Trim();
                }

                if (string.IsNullOrWhiteSpace(raw)) continue;

                if (raw.Equals("-S", StringComparison.OrdinalIgnoreCase))
                {
                    result.IsStop = true;
                }
                else if (raw.Equals("-P", StringComparison.OrdinalIgnoreCase))
                {
                    result.IsPlay = true;
                }
                else if (raw.StartsWith("-N", StringComparison.OrdinalIgnoreCase) && raw.Length > 2)
                {
                    if (int.TryParse(raw.Substring(2), out int m))
                    {
                        result.Measure = Math.Max(0, m);
                    }
                }
                else if (raw.Equals("-EX", StringComparison.OrdinalIgnoreCase) || raw.Equals("-D0", StringComparison.OrdinalIgnoreCase))
                {
                    result.Difficulty = OjnDifficulty.EX;
                }
                else if (raw.Equals("-NX", StringComparison.OrdinalIgnoreCase) || raw.Equals("-D1", StringComparison.OrdinalIgnoreCase))
                {
                    result.Difficulty = OjnDifficulty.NX;
                }
                else if (raw.Equals("-HX", StringComparison.OrdinalIgnoreCase) || raw.Equals("-D2", StringComparison.OrdinalIgnoreCase))
                {
                    result.Difficulty = OjnDifficulty.HX;
                }
                else if (!raw.StartsWith("-"))
                {
                    if (File.Exists(raw))
                    {
                        result.FilePath = raw;
                    }
                    else
                    {
                        try
                        {
                            string full = Path.GetFullPath(raw);
                            if (File.Exists(full))
                            {
                                result.FilePath = full;
                            }
                        }
                        catch { }
                    }
                }
            }

            return result;
        }
    }
}
