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
    public Main main; // Reference to the Main script to control UI elements
    public NoteHighway noteHighway; // Drag the NoteHighway GameObject here in the Inspector
    public bool startMuted = false; // Option to start the audio muted
    public AudioSource audioSource;
    public float songSpeedPercentage = 70f; // Public variable for song speed, default to 70%
    public float currentSongSpeedPercentage = 70f; // Track the actual speed being played

    private string _lastDirectory = ""; // Stores the directory of the last opened PSARC file
    private string _originalAudioPath = ""; // Stores the path to the original, unstretched audio file
    private ArrangementData _arrangementData; // Stores the parsed arrangement data (notes and bar times)

    public ArrangementData ArrangementData => _arrangementData;

    public void SetSongSpeed(float percentage)
    {
        currentSongSpeedPercentage = percentage;
        float tempoChange = percentage - 100f; // Calculate the tempo change for soundstretch
        ChangeTempo(tempoChange);
    }

    public void JumpToTime(float time)
    {
        if (audioSource?.clip == null) return;

        // Clamp time to clip length
        float clampedTime = Mathf.Clamp(time, 0f, audioSource.clip.length);
        audioSource.time = clampedTime;

        // If the audio is paused, ensure the NoteHighway updates its position immediately
        if (!audioSource.isPlaying && noteHighway != null)
        {
            noteHighway.UpdateHighwayPosition();
        }
    }

    public void ChangeTempo(float tempoChange)
    {
        UnityEngine.Debug.Log($"ChangeTempo called with tempoChange: {tempoChange}");
        if (string.IsNullOrEmpty(_originalAudioPath) || !File.Exists(_originalAudioPath))
        {
            UnityEngine.Debug.LogWarning("Original audio path not set or file not found. Cannot change tempo.");
            return;
        }

        main.SetLoadingText(true);

        float currentTime = 0f;
        bool wasPlaying = false;

        if (audioSource != null)
        {
            wasPlaying = audioSource.isPlaying;
            currentTime = audioSource.time;
            audioSource.Stop();
            UnityEngine.Debug.Log($"Audio was playing: {wasPlaying}, current time: {currentTime}");
        }

        UnityEngine.Debug.Log($"PsarcLoader.currentSongSpeedPercentage: {currentSongSpeedPercentage}");

        string stretchedAudioPath = SoundStretch.Process(_originalAudioPath, tempoChange);
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
        
        main.SetLoadingText(false);
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
        noteHighway = FindObjectOfType<NoteHighway>();
        if (main == null)
        {
            main = FindObjectOfType<Main>();
        }
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
        if (main != null)
        {
            main.SetLoadingText(true);
        }

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
                if (main != null)
                {
                    main.SetLoadingText(false);
                }
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

        _lastDirectory = Path.GetDirectoryName(psarcFilePath);

        if (noteHighway != null)
        {
            // Call the new reset method
            noteHighway.ResetHighway();
        }

        try
        {
            using (var psarc = new PsarcFile(psarcFilePath))
            {
                UnityEngine.Debug.Log("Opened PSARC file: " + psarcFilePath);

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

                    _originalAudioPath = fileToLoadPath;
                }
                else
                {
                    UnityEngine.Debug.LogError("No audio file (.ogg or .wem) found in PSARC.");
                    return;
                }

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

                SetSongSpeed(songSpeedPercentage);
                StartCoroutine(LoadAudioAndNotes(_originalAudioPath, arrangementTempPath));
            }
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError("Error parsing PSARC file: " + e.Message);
            main.SetLoadingText(false);
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

        if (Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor)
        {
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
        
        Process process = new Process();
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
                FileInfo wavFileInfo = new FileInfo(wavFilePath);
                if (!wavFileInfo.Exists || wavFileInfo.Length == 0)
                {
                    UnityEngine.Debug.LogError($"WEM conversion succeeded (Exit Code 0) but output WAV file is missing or empty: {wavFilePath}");
                    return false;
                }
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
        string uri = new System.Uri(audioFilePath).AbsoluteUri;
        string extension = Path.GetExtension(audioFilePath).ToLower();
        AudioType audioType = AudioType.UNKNOWN;

        if (extension == ".wav") audioType = AudioType.WAV;

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
                
                _arrangementData = ArrangementParser.ParseArrangement(arrangementFilePath);
                
                if (noteHighway != null)
                {
                    noteHighway.psarcLoader = this;
                    noteHighway.notes = _arrangementData.Notes;
                    // Use the new method to set bar times and create marker data
                    noteHighway.SetBarTimes(_arrangementData.BarTimes);
                    noteHighway.audioSource = audioSource;
                    audioSource.Play();
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

        main.SetLoadingText(false);
    }
}