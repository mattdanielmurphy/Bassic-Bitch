using UnityEngine;
using System.IO;
using System.Collections;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using Rocksmith2014PsarcLib.Psarc;
using UnityEngine.Networking;
using SFB; // Assuming the StandaloneFileBrowser is in the SFB namespace

public class PsarcLoader : MonoBehaviour
{
    public NoteHighway noteHighway; // Drag the NoteHighway GameObject here in the Inspector
    public bool startMuted = false; // Option to start the audio muted
    public AudioSource audioSource;
    [Tooltip("Audio offset in milliseconds. Positive values delay the notes.")]
    public float videoOffsetMs = 0f; // Public variable for audio synchronization offset
    public float tempo = -30f; // Public variable for song speed, hardcoded to 70% (100 - 30)

    private string _lastDirectory = ""; // Stores the directory of the last opened PSARC file
    private string _originalAudioPath = ""; // Stores the path to the original, unstretched audio file

    public void ChangeTempo(float newTempo)
    {
        UnityEngine.Debug.Log($"ChangeTempo called with newTempo: {newTempo}");
        if (string.IsNullOrEmpty(_originalAudioPath) || !File.Exists(_originalAudioPath))
        {
            UnityEngine.Debug.LogWarning("Original audio path not set or file not found. Cannot change tempo.");
            return;
        }

        float currentTime = 0f;
        bool wasPlaying = false;

        if (audioSource != null)
        {
            wasPlaying = audioSource.isPlaying;
            currentTime = audioSource.time;
            audioSource.Stop();
            UnityEngine.Debug.Log($"Audio was playing: {wasPlaying}, current time: {currentTime}");
        }

        this.tempo = newTempo;
        UnityEngine.Debug.Log($"PsarcLoader.tempo set to: {this.tempo}");

        string stretchedAudioPath = SoundStretch.Process(_originalAudioPath, newTempo);
        if (stretchedAudioPath != null)
        {
            UnityEngine.Debug.Log($"SoundStretch returned stretched audio path: {stretchedAudioPath}");
            StartCoroutine(ReloadAudio(stretchedAudioPath, currentTime, wasPlaying));
        }
        else
        {
            UnityEngine.Debug.LogError("SoundStretch failed, falling back to original audio.");
            // Fallback to original audio if stretching fails
            StartCoroutine(ReloadAudio(_originalAudioPath, currentTime, wasPlaying));
        }
    }

    IEnumerator ReloadAudio(string audioPath, float startTime, bool wasPlaying)
    {
        UnityEngine.Debug.Log($"ReloadAudio: Loading audio from: {audioPath}");
        UnityEngine.Debug.Log($"ReloadAudio: Start time: {startTime}, Was playing: {wasPlaying}");

        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(new System.Uri(audioPath).AbsoluteUri, AudioType.WAV))
        {
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                if (clip != null)
                {
                    audioSource.clip = clip;
                    audioSource.time = startTime;
                    if (wasPlaying)
                    {
                        audioSource.Play();
                    }
                    UnityEngine.Debug.Log($"ReloadAudio: Successfully reloaded audio. Clip length: {clip.length}");
                }
                else
                {
                    UnityEngine.Debug.LogError("ReloadAudio: Failed to reload audio clip (GetContent returned null).");
                }
            }
            else
            {
                UnityEngine.Debug.LogError($"ReloadAudio: Error loading stretched audio: {www.error}");
            }
        }
    }

    public void TogglePlayback()
    {
        if (audioSource == null) return;

        if (audioSource.isPlaying)
        {
            audioSource.Pause();
            // UnityEngine.Debug.Log("Audio Paused");
        }
        else
        {
            audioSource.Play();
            // UnityEngine.Debug.Log("Audio Played");
        }
    }

    void Start()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.mute = startMuted;
        OpenPsarcFileBrowser();
    }

    public void NewSong()
    {
        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
        }
        OpenPsarcFileBrowser();
    }

    public void OpenPsarcFileBrowser()
    {
        var extensions = new [] {
            new ExtensionFilter("Rocksmith PSARC Files", "psarc" ),
            new ExtensionFilter("All Files", "*" ),
        };
        
        // Use OpenFilePanelAsync for non-blocking UI
        StandaloneFileBrowser.OpenFilePanelAsync("Open Rocksmith PSARC File", _lastDirectory, extensions, false, (string[] paths) => {
            if (paths.Length > 0 && !string.IsNullOrEmpty(paths[0]))
            {
                LoadPsarc(paths[0]);
            }
            else
            {
                UnityEngine.Debug.Log("File selection cancelled or failed.");
            }
        });
    }

    private void LoadPsarc(string psarcFilePath)
    {
        if (!File.Exists(psarcFilePath))
        {
            UnityEngine.Debug.LogError("PSARC file does not exist: " + psarcFilePath);
            return;
        }

        // Save the directory of the loaded file for the next file browser open
        _lastDirectory = Path.GetDirectoryName(psarcFilePath);

        // Reset the NoteHighway immediately to clear old notes and stop processing
        if (noteHighway != null)
        {
            noteHighway.ResetNotes();
        }

        try
        {
            using (var psarc = new PsarcFile(psarcFilePath))
            {
                UnityEngine.Debug.Log("Opened PSARC file: " + psarcFilePath);

                // 1. Find and extract audio
                var audioEntry = psarc.TOC.Entries.FirstOrDefault(e => e.Path != null && (e.Path.EndsWith(".ogg") || e.Path.EndsWith(".wem")));
                string fileToLoadPath = null;

                if (audioEntry != null)
                {
                    string audioName = Path.GetFileName(audioEntry.Path);
                    string tempPath = Path.Combine(Application.temporaryCachePath, audioName);
                    
                    using (var outFile = new FileStream(tempPath, FileMode.Create))
                    {
                        psarc.InflateEntry(audioEntry, outFile);
                    }

                    string extension = Path.GetExtension(tempPath).ToLower();

                    if (extension == ".wem")
                    {
                        string wavPath = Path.ChangeExtension(tempPath, ".wav");
                        if (File.Exists(wavPath))
                        {
                            fileToLoadPath = wavPath;
                        }
                        else
                        {
                            if (ConvertWemToWav(tempPath, wavPath))
                            {
                                fileToLoadPath = wavPath;
                            }
                            else
                            {
                                UnityEngine.Debug.LogError("WEM conversion failed. Cannot load audio.");
                                return;
                            }
                        }
                    }
                    else
                    {
                        fileToLoadPath = tempPath;
                    }

                    _originalAudioPath = fileToLoadPath; // Store the original path
                }
                else
                {
                    UnityEngine.Debug.LogError("No audio file (.ogg or .wem) found in PSARC.");
                    return;
                }

                // 2. Find and extract arrangement XML
                var arrangementEntry = psarc.TOC.Entries.FirstOrDefault(e => e.Path != null && e.Path.EndsWith("_bass.xml"));
                string arrangementTempPath = null;

                if (arrangementEntry != null) {
                    arrangementTempPath = Path.Combine(
                        Application.temporaryCachePath, Path.GetFileName(arrangementEntry.Path));
                    using (var outFile = new FileStream(arrangementTempPath, FileMode.Create)) {
                        psarc.InflateEntry(arrangementEntry, outFile);
                    }
                }
                else {
                    UnityEngine.Debug.LogError("Bass arrangement XML not found in PSARC!");
                    return;
                }

                // 3. Start the asynchronous loading of audio and notes
                string stretchedAudioPath = SoundStretch.Process(fileToLoadPath, tempo);
                if (stretchedAudioPath != null)
                {
                    StartCoroutine(LoadAudioAndNotes(stretchedAudioPath, arrangementTempPath));
                }
                else
                {
                    // Fallback to original audio if stretching fails
                    StartCoroutine(LoadAudioAndNotes(fileToLoadPath, arrangementTempPath));
                }
            }
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError("Error parsing PSARC file: " + e.Message);
        }
    }

    private bool ConvertWemToWav(string wemFilePath, string wavFilePath)
    {
        string toolDir = Path.Combine(Application.streamingAssetsPath, "tools");
        string cliPath = "";

#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        cliPath = Path.Combine(toolDir, "mac/vgmstream-cli");
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        cliPath = Path.Combine(toolDir, "windows/vgmstream-cli.exe");
#elif UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
        cliPath = Path.Combine(toolDir, "linux/vgmstream-cli");
#else
        UnityEngine.Debug.LogError("Unsupported platform for vgmstream-cli.");
        return false;
#endif
        
        if (!File.Exists(cliPath))
        {
            UnityEngine.Debug.LogError($"vgmstream-cli not found at: {cliPath}. Place the correct binary there.");
            return false;
        }

        // On macOS/Linux, ensure the executable has execute permissions
        if (Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor)
        {
            // This command is executed via the shell to ensure permissions are set
            Process chmodProcess = new Process();
            chmodProcess.StartInfo.FileName = "/bin/bash";
            chmodProcess.StartInfo.Arguments = $"-c \"chmod +x \\\"{cliPath}\\\"\"";
            chmodProcess.StartInfo.UseShellExecute = false;
            chmodProcess.StartInfo.RedirectStandardOutput = true;
            chmodProcess.StartInfo.RedirectStandardError = true;
            chmodProcess.Start();
            chmodProcess.WaitForExit();
            
            if (chmodProcess.ExitCode != 0)
            {
                UnityEngine.Debug.LogError($"Failed to set execute permissions on vgmstream-cli. STDERR: {chmodProcess.StandardError.ReadToEnd()}");
                return false;
            }
        }

        // UnityEngine.Debug.Log($"Converting WEM to WAV using: {cliPath}");
        
        Process process = new Process();
        // Command: vgmstream-cli -o <output> <input>
        process.StartInfo.FileName = cliPath;
        process.StartInfo.Arguments = $"-o \"{wavFilePath}\" \"{wemFilePath}\"";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
        
        try
        {
            process.Start();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                // Check if the output file was actually created and has content
                FileInfo wavFileInfo = new FileInfo(wavFilePath);
                if (!wavFileInfo.Exists || wavFileInfo.Length == 0)
                {
                    UnityEngine.Debug.LogError($"WEM conversion succeeded (Exit Code 0) but output WAV file is missing or empty: {wavFilePath}");
                    return false;
                }

                // UnityEngine.Debug.Log($"WEM conversion successful. Output file size: {wavFileInfo.Length} bytes.");
                // Log output for debugging
                // UnityEngine.Debug.Log($"vgmstream-cli STDOUT: {process.StandardOutput.ReadToEnd()}");
                return true;
            }
            else
            {
                UnityEngine.Debug.LogError($"WEM conversion failed with exit code {process.ExitCode}.\nSTDERR: {process.StandardError.ReadToEnd()}");
                return false;
            }
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError($"Error starting vgmstream-cli process: {e.Message}");
            return false;
        }
    }

    IEnumerator LoadAudioAndNotes(string audioFilePath, string arrangementFilePath)
    {
        // 1. Load Audio
        string uri = new System.Uri(audioFilePath).AbsoluteUri;
        string extension = Path.GetExtension(audioFilePath).ToLower();
        AudioType audioType = AudioType.UNKNOWN;

        if (extension == ".wav")
        {
            audioType = AudioType.WAV;
        }

        if (!File.Exists(audioFilePath))
        {
            UnityEngine.Debug.LogError($"Audio file not found at path: {audioFilePath}");
            yield break;
        }

        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, audioType))
        {
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                if (clip == null)
                {
                    UnityEngine.Debug.LogError("DownloadHandlerAudioClip.GetContent returned null. Cannot load audio.");
                    yield break;
                }
                audioSource.clip = clip;
                
                // 2. Parse Notes (Synchronous)
                List<NoteData> notes = ArrangementParser.ParseArrangement(arrangementFilePath);
                
                // 3. Assign Notes and Start Playback (Ensures notes are ready right before audio starts)
                if (noteHighway != null)
                {
                    noteHighway.notes = notes;
                    noteHighway.audioSource = audioSource;
                    noteHighway.videoOffsetMs = videoOffsetMs; // Pass the offset
                    audioSource.Play();
                    // UnityEngine.Debug.Log("Playing audio: " + audioFilePath);
                }
                else
                {
                    UnityEngine.Debug.LogError("NoteHighway not set in Inspector! Cannot start song.");
                }
            }
            else
            {
                UnityEngine.Debug.LogError("Error loading audio: " + www.error);
            }
        }
    }
}
