using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace O2Play
{
    public static class SongLoader
    {
        private static bool _isDialogOpen = false;

        public static void OpenSongDialog(IntPtr hwnd, Action<string> onFileSelected)
        {
            if (_isDialogOpen) return;
            _isDialogOpen = true;

            var dialogThread = new Thread(() =>
            {
                try
                {
                    using var ofd = new OpenFileDialog
                    {
                        Title = "Open File",
                        Filter = "Chart Files (*.bms;*.bme;*.bml;*.ojn)|*.bms;*.bme;*.bml;*.ojn|All Files (*.*)|*.*",
                        RestoreDirectory = true
                    };

                    if (ofd.ShowDialog(new Win32WindowWrapper(hwnd)) == DialogResult.OK)
                    {
                        onFileSelected(ofd.FileName);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to open file: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    _isDialogOpen = false;
                }
            });
            dialogThread.SetApartmentState(ApartmentState.STA);
            dialogThread.Start();
        }

        public static BmsChart? LoadSong(string filePath, AudioEngine audio, OjnDifficulty? preferredDifficulty, IntPtr hwnd)
        {
            if (filePath.EndsWith(".ojn", StringComparison.OrdinalIgnoreCase))
            {
                return LoadOjnSong(filePath, audio, preferredDifficulty, hwnd);
            }
            else
            {
                return LoadBmsSong(filePath, audio, hwnd);
            }
        }

        private static BmsChart? LoadOjnSong(string filePath, AudioEngine audio, OjnDifficulty? preferredDifficulty, IntPtr hwnd)
        {
            string ojmPath = OjnParser.FindOjmPath(filePath);

            bool isOjnWithCachedAudio = !string.IsNullOrEmpty(ojmPath)
                && File.Exists(ojmPath)
                && audio.HasArchiveLoaded(ojmPath);

            if (!isOjnWithCachedAudio)
            {
                audio.Clear();
            }

            var chart = OjnParser.Parse(filePath, audio, preferredDifficulty);

            if (!File.Exists(ojmPath) && chart.Header.Wavs.Count == 0)
            {
                string ojmFileName = Path.GetFileName(ojmPath);
                using var ofd = new OpenFileDialog
                {
                    Title = $"Locate Missing File: {ojmFileName}",
                    Filter = "O2Jam Audio Archive (*.ojm)|*.ojm|All Files (*.*)|*.*",
                    InitialDirectory = Path.GetDirectoryName(filePath) ?? "",
                    RestoreDirectory = true
                };

                try { ofd.FileName = ojmFileName; } catch { }

                if (ofd.ShowDialog(new Win32WindowWrapper(hwnd)) == DialogResult.OK && File.Exists(ofd.FileName))
                {
                    var samples = OjmDecoder.LoadArchive(ofd.FileName);
                    audio.LoadArchiveSamples(ofd.FileName, samples);
                    foreach (var kvp in samples)
                    {
                        chart.Header.Wavs[kvp.Key] = $"ojm_{kvp.Key}.wav";
                    }
                }
                else
                {
                    var result = MessageBox.Show(
                        new Win32WindowWrapper(hwnd),
                        $"Matching .ojm audio archive was not located for this chart:\n\n{Path.GetFileName(filePath)}\n\nContinue loading chart without sound?",
                        "Missing OJM Audio Archive",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (result != DialogResult.Yes)
                    {
                        return null;
                    }
                }
            }

            return chart;
        }

        private static BmsChart? LoadBmsSong(string filePath, AudioEngine audio, IntPtr hwnd)
        {
            audio.Clear();

            var chart = BmsParser.Parse(filePath);

            string songDirectory = Path.GetDirectoryName(filePath) ?? "";
            var resolvedSounds = new Dictionary<int, string>();
            var missingSounds = new List<KeyValuePair<int, string>>();

            string canonicalDir = Path.GetFullPath(songDirectory);
            foreach (var kvp in chart.Header.Wavs)
            {
                string rel = kvp.Value.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
                string full = Path.GetFullPath(Path.Combine(songDirectory, rel));
                if (!full.StartsWith(canonicalDir, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool found = File.Exists(full);

                if (!found)
                {
                    string fileName = Path.GetFileNameWithoutExtension(rel);
                    string? subDir = Path.GetDirectoryName(rel);
                    string searchDir = string.IsNullOrEmpty(subDir) ? songDirectory : Path.Combine(songDirectory, subDir);
                    string canonicalSearchDir = Path.GetFullPath(searchDir);
                    if (canonicalSearchDir.StartsWith(canonicalDir, StringComparison.OrdinalIgnoreCase) && Directory.Exists(canonicalSearchDir))
                    {
                        string[] candidates = { fileName + ".mp3", fileName + ".wav", fileName + ".ogg" };
                        foreach (var cand in candidates)
                        {
                            string candPath = Path.Combine(searchDir, cand);
                            if (File.Exists(candPath))
                            {
                                full = candPath;
                                found = true;
                                break;
                            }
                        }

                        if (!found)
                        {
                            try
                            {
                                var allInSearchDir = Directory.GetFiles(searchDir);
                                foreach (var file in allInSearchDir)
                                {
                                    string fn = Path.GetFileName(file);
                                    string fnNoExt = Path.GetFileNameWithoutExtension(file);
                                    if (fn.Equals(Path.GetFileName(rel), StringComparison.OrdinalIgnoreCase) ||
                                        fnNoExt.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        full = file;
                                        found = true;
                                        break;
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }

                if (found)
                {
                    resolvedSounds[kvp.Key] = full;
                }
                else
                {
                    missingSounds.Add(kvp);
                }
            }

            if (missingSounds.Count > 0)
            {
                string lastLocatedDir = songDirectory;
                bool skipRemaining = false;

                for (int i = 0; i < missingSounds.Count; i++)
                {
                    if (skipRemaining) break;

                    var kvp = missingSounds[i];
                    string missingName = kvp.Value;
                    string cleanFileName = Path.GetFileName(missingName);

                    if (!string.IsNullOrEmpty(lastLocatedDir) && Directory.Exists(lastLocatedDir))
                    {
                        string candidate = Path.Combine(lastLocatedDir, cleanFileName);
                        if (File.Exists(candidate))
                        {
                            resolvedSounds[kvp.Key] = candidate;
                            continue;
                        }

                        string noExt = Path.GetFileNameWithoutExtension(cleanFileName);
                        string[] exts = { ".mp3", ".wav", ".ogg" };
                        bool autoFound = false;
                        foreach (var ext in exts)
                        {
                            string candExt = Path.Combine(lastLocatedDir, noExt + ext);
                            if (File.Exists(candExt))
                            {
                                resolvedSounds[kvp.Key] = candExt;
                                autoFound = true;
                                break;
                            }
                        }
                        if (autoFound) continue;
                    }

                    using var ofd = new OpenFileDialog();
                    ofd.Title = missingSounds.Count > 1
                        ? $"Locate Missing File ({i + 1} of {missingSounds.Count}): {cleanFileName}"
                        : $"Locate Missing File: {cleanFileName}";

                    string extLower = Path.GetExtension(cleanFileName).ToLowerInvariant();
                    if (extLower == ".mp3")
                    {
                        ofd.Filter = "MP3 Audio (*.mp3)|*.mp3|All Audio Files (*.mp3;*.wav;*.ogg)|*.mp3;*.wav;*.ogg|All Files (*.*)|*.*";
                    }
                    else if (extLower == ".ogg")
                    {
                        ofd.Filter = "Ogg Vorbis (*.ogg)|*.ogg|All Audio Files (*.mp3;*.wav;*.ogg)|*.mp3;*.wav;*.ogg|All Files (*.*)|*.*";
                    }
                    else
                    {
                        ofd.Filter = "Audio Files (*.wav;*.mp3;*.ogg)|*.wav;*.mp3;*.ogg|Waveform Audio (*.wav)|*.wav|MP3 Audio (*.mp3)|*.mp3|All Files (*.*)|*.*";
                    }

                    ofd.InitialDirectory = Directory.Exists(lastLocatedDir) ? lastLocatedDir : songDirectory;
                    ofd.RestoreDirectory = true;

                    string safeName = cleanFileName;
                    foreach (char c in Path.GetInvalidFileNameChars())
                    {
                        safeName = safeName.Replace(c, '_');
                    }
                    try { ofd.FileName = safeName; } catch { }

                    var dialogResult = ofd.ShowDialog(new Win32WindowWrapper(hwnd));
                    if (dialogResult == DialogResult.OK && File.Exists(ofd.FileName))
                    {
                        resolvedSounds[kvp.Key] = ofd.FileName;
                        lastLocatedDir = Path.GetDirectoryName(ofd.FileName) ?? lastLocatedDir;
                    }
                    else
                    {
                        string promptMsg = missingSounds.Count > (i + 1)
                            ? $"Could not locate '{cleanFileName}'.\n\nDo you want to continue loading the chart without this file, or cancel?"
                            : $"Could not locate '{cleanFileName}'.\n\nContinue loading chart without this audio file?";

                        var confirmResult = MessageBox.Show(
                            new Win32WindowWrapper(hwnd),
                            promptMsg,
                            "Missing Audio File",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question);

                        if (confirmResult != DialogResult.Yes)
                        {
                            return null;
                        }

                        if (missingSounds.Count > (i + 1))
                        {
                            var skipAllResult = MessageBox.Show(
                                new Win32WindowWrapper(hwnd),
                                $"There are {missingSounds.Count - (i + 1)} more missing audio files.\n\nSkip remaining missing files and load the chart now?",
                                "Skip Remaining Missing Files?",
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Question);

                            if (skipAllResult == DialogResult.Yes)
                            {
                                skipRemaining = true;
                            }
                        }
                    }
                }
            }

            foreach (var kvp in resolvedSounds)
            {
                audio.LoadSound(kvp.Key, kvp.Value);
            }

            if (missingSounds.Count > 0)
            {
                Logger.Warn($"[BMS] {missingSounds.Count} audio file(s) not found");
            }

            return chart;
        }
    }
}

