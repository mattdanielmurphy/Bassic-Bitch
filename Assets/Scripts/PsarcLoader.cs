using UnityEngine;
using System.IO;
using System.Collections;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading.Tasks; // Added for asynchronous SoundStretch
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
    private string _currentPsarcFilePath = ""; // Stores the path to the currently loaded PSARC file
    private string _currentSongCacheDir = ""; // Stores the cache directory of the currently loaded PSARC file

    public ArrangementData ArrangementData => _arrangementData;
    public bool IsReloading { get; private set; } = false;

    [System.Serializable]
    private class SongCacheInfo
    {
        public string PsarcFilePath;
        public string SongName;
    }

    /// <summary>
    /// Generates a unique, persistent directory path for a given PSARC file.
    /// </summary>
    /// <param name="psarcFilePath">The path to the PSARC file.</param>
    /// <returns>The full path to the song's cache directory.</returns>
    private string GetSongCacheDirectory(string psarcFilePath)
    {
        if (string.IsNullOrEmpty(psarcFilePath)) return null;

        // Use a hash of the full path to create a unique, safe directory name
        string dirName = System.Security.Cryptography.SHA256.Create()
            .ComputeHash(System.Text.Encoding.UTF8.GetBytes(psarcFilePath))
            .Select(b => b.ToString("x2")).Aggregate((a, b) => a + b);

        string cacheDir = Path.Combine(Application.persistentDataPath, "SongCache", dirName);
        if (!Directory.Exists(cacheDir))
        {
            Directory.CreateDirectory(cacheDir);
        }
        
        // Save song info for human-readable logging (always write to ensure it's present for old caches)
        string infoPath = Path.Combine(cacheDir, "song_info.json");
        try
        {
            SongCacheInfo info = new SongCacheInfo
            {
                PsarcFilePath = psarcFilePath,
                SongName = Path.GetFileNameWithoutExtension(psarcFilePath)
            };
            string json = JsonUtility.ToJson(info);
            File.WriteAllText(infoPath, json);
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogWarning($"Failed to write song info to cache directory {cacheDir}: {e.Message}");
        }

        return cacheDir;
    }

    /// <summary>
    /// Gets the expected paths for the cached WAV and XML files.
    /// </summary>
    /// <param name="psarcFilePath">The path to the PSARC file.</param>
    /// <param name="wavPath">Output: The expected path for the cached WAV file.</param>
    /// <param name="xmlPath">Output: The expected path for the cached XML file.</param>
    private void GetCachedFilePaths(string psarcFilePath, out string wavPath, out string xmlPath)
    {
        string cacheDir = GetSongCacheDirectory(psarcFilePath);
        // Use fixed names for the cached files within the unique directory
        wavPath = Path.Combine(cacheDir, "audio.wav");
        xmlPath = Path.Combine(cacheDir, "arrangement.xml");
    }

    /// <summary>
    /// Generates a unique, persistent file path for song settings based on the PSARC file path.
    /// </summary>
    /// <param name="psarcFilePath">The path to the PSARC file.</param>
    /// <returns>The full path to the settings file.</returns>
    private string GetSettingsFilePath(string psarcFilePath)
    {
        if (string.IsNullOrEmpty(psarcFilePath)) return null;

        // Use a hash of the full path to create a unique, safe filename
        string fileName = System.Security.Cryptography.SHA256.Create()
            .ComputeHash(System.Text.Encoding.UTF8.GetBytes(psarcFilePath))
            .Select(b => b.ToString("x2")).Aggregate((a, b) => a + b);

        string settingsDir = Path.Combine(Application.persistentDataPath, "SongSettings");
        if (!Directory.Exists(settingsDir))
        {
            Directory.CreateDirectory(settingsDir);
        }

        return Path.Combine(settingsDir, fileName + ".json");
    }

    /// <summary>
    /// Loads song settings from the persistent cache.
    /// </summary>
    /// <param name="psarcFilePath">The path to the PSARC file.</param>
    /// <returns>The loaded SongSettings object, or a new one with defaults if loading fails.</returns>
    private SongSettings LoadSongSettings(string psarcFilePath)
    {
        string settingsPath = GetSettingsFilePath(psarcFilePath);
        if (File.Exists(settingsPath))
        {
            try
            {
                string json = File.ReadAllText(settingsPath);
                return JsonUtility.FromJson<SongSettings>(json);
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError($"Failed to load song settings from {settingsPath}: {e.Message}");
                // Fallback to default settings
                return new SongSettings();
            }
        }
        // File doesn't exist, return default settings
        return new SongSettings();
    }

    /// <summary>
    /// Saves the current arrangement data's speed to the persistent cache.
    /// </summary>
    /// <param name="data">The arrangement data containing the speed to save.</param>
    public void SaveSongSettings(ArrangementData data)
    {
        if (data == null || string.IsNullOrEmpty(_currentPsarcFilePath)) return;

        string settingsPath = GetSettingsFilePath(_currentPsarcFilePath);
        
        try
        {
            SongSettings settings = new SongSettings { LastPlayedSpeed = data.LastPlayedSpeed };
            string json = JsonUtility.ToJson(settings, true); // 'true' for pretty printing
            File.WriteAllText(settingsPath, json);
            UnityEngine.Debug.Log($"Saved song settings to: {settingsPath}");
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError($"Failed to save song settings to {settingsPath}: {e.Message}");
        }
    }

    public void SetSongSpeed(float newPercentage, float oldPercentage)
    {
        currentSongSpeedPercentage = newPercentage;
        float tempoChange = newPercentage - 100f; // Calculate the tempo change for soundstretch
        StartCoroutine(ChangeTempoAndReload(tempoChange, oldPercentage));
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

    IEnumerator ChangeTempoAndReload(float tempoChange, float oldPercentage)
    {
        UnityEngine.Debug.Log($"ChangeTempoAndReload called with tempoChange: {tempoChange}");
        
        string originalWavPath;
        if (!EnsureOriginalAudioIsCached(_currentPsarcFilePath, out originalWavPath) || Path.GetExtension(originalWavPath).ToLower() != ".wav")
        {
            UnityEngine.Debug.LogWarning("Original audio file is not a WAV or could not be loaded/recreated. Cannot change tempo.");
            yield break;
        }
        
        // Update the original audio path field in case it was recreated
        _originalAudioPath = originalWavPath;

        IsReloading = true; // Signal that a reload is starting

        // 1. Show loading text immediately and yield a frame to ensure UI update
        main.SetLoadingText(true);
        yield return null; // Wait one frame for the UI to update

        bool wasPlaying = false;
        float currentTime = 0f;
        float newTime = 0f;

        if (audioSource != null)
        {
            wasPlaying = audioSource.isPlaying;
            currentTime = audioSource.time;
            audioSource.Pause(); // Use Pause() instead of Stop() to preserve the current time
            UnityEngine.Debug.Log($"Audio was playing: {wasPlaying}, current time: {currentTime}");

            // Calculate the new time to maintain musical position: T_new = T_old * (S_old / S_new)
            float oldSpeedMultiplier = oldPercentage / 100f;
            float newSpeedMultiplier = currentSongSpeedPercentage / 100f;
            
            // Only adjust time if speed actually changed
            if (Mathf.Abs(oldSpeedMultiplier - newSpeedMultiplier) > 0.001f)
            {
                newTime = currentTime * (oldSpeedMultiplier / newSpeedMultiplier);
                UnityEngine.Debug.Log($"Adjusting time: {currentTime:F2}s * ({oldSpeedMultiplier:F2} / {newSpeedMultiplier:F2}) = {newTime:F2}s");
            }
            else
            {
                newTime = currentTime;
            }

            // Clamp to current clip length as a failsafe before reload
            if (audioSource.clip != null)
            {
                // The new clip length will be oldClipLength * (oldSpeedMultiplier / newSpeedMultiplier)
                float maxNewTime = audioSource.clip.length * (oldSpeedMultiplier / newSpeedMultiplier);
                newTime = Mathf.Clamp(newTime, 0f, maxNewTime);
            }
        }
        else
        {
            newTime = 0f;
        }

        UnityEngine.Debug.Log($"PsarcLoader.currentSongSpeedPercentage: {currentSongSpeedPercentage}");

        string audioToLoadPath = _originalAudioPath;
        string stretchedAudioPath = null;

        // Only run SoundStretch if there is an actual tempo change
        if (Mathf.Abs(tempoChange) > 0.01f) // Use a small epsilon for float comparison
        {
            // Determine the cache path for the stretched audio
            string cacheDir = GetSongCacheDirectory(_currentPsarcFilePath);
            // Filename: audio_[speed].wav, e.g., audio_70.wav
            string stretchedCachePath = Path.Combine(cacheDir, $"audio_{currentSongSpeedPercentage:F0}.wav");

            if (File.Exists(stretchedCachePath))
            {
                UnityEngine.Debug.Log($"Found cached stretched audio for {currentSongSpeedPercentage:F0}% speed: {stretchedCachePath}");
                audioToLoadPath = stretchedCachePath;
            }
            else
            {
                UnityEngine.Debug.Log("Starting SoundStretch asynchronously...");
                
                // Run SoundStretch asynchronously
                var stretchTask = Task.Run(() => 
                {
                    return SoundStretch.Process(_originalAudioPath, stretchedCachePath, tempoChange);
                });

                // Wait for the task to complete without blocking the main thread
                yield return new WaitUntil(() => stretchTask.IsCompleted);

                if (stretchTask.IsFaulted)
                {
                    UnityEngine.Debug.LogError($"SoundStretch task failed: {stretchTask.Exception}");
                }
                else
                {
                    stretchedAudioPath = stretchTask.Result;
                }

                if (stretchedAudioPath != null)
                {
                    UnityEngine.Debug.Log($"SoundStretch returned stretched audio path: {stretchedAudioPath}");
                    audioToLoadPath = stretchedAudioPath;
                }
                else
                {
                    UnityEngine.Debug.LogError("SoundStretch failed, falling back to original audio.");
                    // audioToLoadPath remains _originalAudioPath
                }
            }
        }
        else
        {
            UnityEngine.Debug.Log("Tempo change is 0%. Skipping SoundStretch and loading original audio.");
            // audioToLoadPath remains _originalAudioPath
        }

        CleanSongCache(audioToLoadPath);

        // Reload audio
        yield return StartCoroutine(ReloadAudio(audioToLoadPath, newTime, wasPlaying));
        
        // 2. Hide loading text after reload is complete
        main.SetLoadingText(false);
        IsReloading = false; // Signal that the reload is complete
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
                    // Update the file's last access time to reflect it was just played
                    try
                    {
                        File.SetLastAccessTimeUtc(audioPath, System.DateTime.UtcNow);
                        UnityEngine.Debug.Log($"Updated LastAccessTime for: {Path.GetFileName(audioPath)}");
                    }
                    catch (System.Exception e)
                    {
                        UnityEngine.Debug.LogWarning($"Failed to update LastAccessTime for {audioPath}: {e.Message}");
                    }
                    
                    audioSource.clip = clip;
                    audioSource.time = startTime;
                    // Unity workaround: always Play so audioSource.time is valid, then immediate Pause if not supposed to play
                    audioSource.Play();
                    if (!wasPlaying)
                        audioSource.Pause();
                    UnityEngine.Debug.Log($"ReloadAudio: Successfully reloaded audio. Clip length: {clip.length}");
                    
                    // Wait a frame to ensure audioSource.time is fully propagated by Unity
                    yield return null;
                    
                    // Verify that the time was properly restored (or is at least not 0 if we're not at the start)
                    // This ensures NoteHighway won't see time=0 when it shouldn't
                    if (audioSource.time == 0f && startTime > 0.1f)
                    {
                        // Time didn't restore properly, try setting it again
                        audioSource.time = startTime;
                        yield return null; // Wait another frame
                    }
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
        
        if (noteHighway != null)
        {
            noteHighway.UpdateHighwayPosition();
        }

        IsReloading = false; // Signal that the reload is complete
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

        _currentPsarcFilePath = psarcFilePath;
        _lastDirectory = Path.GetDirectoryName(psarcFilePath);

        // Ensure the cache directory and song_info.json are created/updated
        string cacheDir = GetSongCacheDirectory(psarcFilePath);

        if (noteHighway != null)
        {
            noteHighway.ResetHighway();
        }

        string cachedWavPath, cachedXmlPath;
        GetCachedFilePaths(psarcFilePath, out cachedWavPath, out cachedXmlPath);

        // Check for both cached files. This initial check prevents PSARC file open/re-extraction if both exist.
        if (File.Exists(cachedWavPath) && File.Exists(cachedXmlPath))
        {
            UnityEngine.Debug.Log("Found cached files. Loading from cache: " + cachedWavPath);
            _originalAudioPath = cachedWavPath;
            StartCoroutine(LoadAudioAndNotes(cachedWavPath, cachedXmlPath));
            return;
        }

        // Ensure original audio is cached as WAV
        bool audioIsWavCached = EnsureOriginalAudioIsCached(psarcFilePath, out cachedWavPath);

        // If it failed to cache as WAV (e.g., OGG only), we still need the path for playback if it's not null.
        // cachedWavPath contains the path to the temporary OGG file in this case.
        if (cachedWavPath == null || !File.Exists(cachedWavPath))
        {
            UnityEngine.Debug.LogError("Failed to load or cache original audio from PSARC.");
            return;
        }

        // If XML not cached, proceed with unpacking
        string fileToLoadPath = cachedWavPath;
        string arrangementTempPath = null;

        try
        {
            using (var psarc = new PsarcFile(psarcFilePath))
            {
                UnityEngine.Debug.Log("Opened PSARC file for XML extraction: " + psarcFilePath);
                
                // 2. Handle Arrangement XML
                var arrangementEntry = psarc.TOC.Entries.FirstOrDefault(e => e.Path != null && e.Path.EndsWith("_bass.xml"));

                if (arrangementEntry != null) {
                    arrangementTempPath = Path.Combine(
                        Application.temporaryCachePath, Path.GetFileName(arrangementEntry.Path));
                    using (var outFile = new FileStream(arrangementTempPath, FileMode.Create)) {
                        psarc.InflateEntry(arrangementEntry, outFile);
                    }
                    
                    // Save the unpacked XML to the persistent cache
                    // Only cache XML if we have a WAV file (which means speed change is possible)
                    if (audioIsWavCached) 
                    {
                        File.Copy(arrangementTempPath, cachedXmlPath, true);
                        UnityEngine.Debug.Log("Cached XML to: " + cachedXmlPath);
                        CleanSongCache(cachedWavPath); // Clean up the cache after a new song has been added, protecting the newly cached original audio.
                    }
                    
                    StartCoroutine(LoadAudioAndNotes(fileToLoadPath, arrangementTempPath));
                }
                else {
                    UnityEngine.Debug.LogError("Bass arrangement XML not found in PSARC!");
                    return;
                }
            }
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError("Error parsing PSARC file: " + e.Message);
            main.SetLoadingText(false);
        }

        _originalAudioPath = cachedWavPath; // Set this after successful processing
    }

    /// <summary>
    /// Ensures that the original audio file is present in the cache as a WAV, 
    /// recreating it from the PSARC if necessary.
    /// </summary>
    /// <param name="psarcFilePath">Path to the PSARC file.</param>
    /// <param name="cachedWavPath">Output: The path to the cached original audio WAV file.</param>
    /// <returns>True if the original audio WAV is found or successfully created/cached, false otherwise.</returns>
    private bool EnsureOriginalAudioIsCached(string psarcFilePath, out string cachedWavPath)
    {
        cachedWavPath = null;
        string cachedXmlPath;
        GetCachedFilePaths(psarcFilePath, out cachedWavPath, out cachedXmlPath);

        // 1. Check if the WAV is already in the cache
        if (File.Exists(cachedWavPath))
        {
            UnityEngine.Debug.Log($"Found cached original audio at: {cachedWavPath}");
            return true;
        }

        // 2. If not cached, proceed with unpacking and conversion
        try
        {
            using (var psarc = new PsarcFile(psarcFilePath))
            {
                UnityEngine.Debug.Log("Opened PSARC file for original audio extraction: " + psarcFilePath);

                // Handle Audio (WEM/OGG -> WAV)
                var audioEntry = psarc.TOC.Entries.FirstOrDefault(e => e.Path != null && (e.Path.EndsWith(".ogg") || e.Path.EndsWith(".wem")));

                if (audioEntry != null)
                {
                    string audioName = Path.GetFileName(audioEntry.Path);
                    string tempAudioPath = Path.Combine(Application.temporaryCachePath, audioName);
                    
                    // Unpack to temporary location first
                    using (var outFile = new FileStream(tempAudioPath, FileMode.Create))
                    {
                        psarc.InflateEntry(audioEntry, outFile);
                    }

                    string extension = Path.GetExtension(tempAudioPath).ToLower();

                    if (extension == ".wem")
                    {
                        string tempWavPath = Path.ChangeExtension(tempAudioPath, ".wav");
                        
                        // Convert WEM to WAV
                        if (ConvertWemToWav(tempAudioPath, tempWavPath))
                        {
                            // Save the converted WAV to the persistent cache
                            File.Copy(tempWavPath, cachedWavPath, true);
                            UnityEngine.Debug.Log("Cached original WAV to: " + cachedWavPath);
                            return true;
                        }
                        else
                        {
                            UnityEngine.Debug.LogError("WEM conversion failed. Cannot load audio.");
                            return false;
                        }
                    }
                    else // OGG or other supported format
                    {
                        // OGG is not WAV, and SoundStretch requires WAV. This path is only for a song that can't be stretched.
                        // We will not cache it as WAV, and we will not return true if speed change is needed later.
                        UnityEngine.Debug.LogWarning("OGG audio found. Not caching audio. Speed change feature may not work as it requires WAV.");
                        cachedWavPath = tempAudioPath; // Temporarily assign OGG path
                        return false; // Return false because we couldn't ensure a WAV cache
                    }
                }
                else
                {
                    UnityEngine.Debug.LogError("No audio file (.ogg or .wem) found in PSARC.");
                    return false;
                }
            }
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError("Error processing original audio from PSARC file: " + e.Message);
            return false;
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
        // First, parse the arrangement to determine the required song speed
        _arrangementData = ArrangementParser.ParseArrangement(arrangementFilePath);
        
        // Load persistent settings to get the last played speed
        SongSettings settings = LoadSongSettings(_currentPsarcFilePath);
        _arrangementData.LastPlayedSpeed = settings.LastPlayedSpeed;
        float speedToApply = _arrangementData.LastPlayedSpeed;
        currentSongSpeedPercentage = speedToApply; // Update current speed tracker

        // Determine which audio file to load (original or speed-adjusted)
        string audioPathToLoad = audioFilePath; // Default to original
        float tempoChange = speedToApply - 100f;

        if (Mathf.Abs(tempoChange) > 0.01f)
        {
            string cacheDir = GetSongCacheDirectory(_currentPsarcFilePath);
            string stretchedCachePath = Path.Combine(cacheDir, $"audio_{speedToApply:F0}.wav");

            if (File.Exists(stretchedCachePath))
            {
                UnityEngine.Debug.Log($"Found cached stretched audio for {speedToApply:F0}% speed: {stretchedCachePath}");
                audioPathToLoad = stretchedCachePath;
            }
            else
            {
                UnityEngine.Debug.Log($"No cached audio for {speedToApply:F0}%, creating it now...");
                string stretchedAudioPath = SoundStretch.Process(audioFilePath, stretchedCachePath, tempoChange);
                if (stretchedAudioPath != null)
                {
                    audioPathToLoad = stretchedAudioPath;
                    CleanSongCache(stretchedAudioPath); // Clean cache since we added a new file, protecting the newly created stretched audio.
                }
                else
                {
                    UnityEngine.Debug.LogError("SoundStretch failed, falling back to original audio.");
                    audioPathToLoad = audioFilePath; // Explicitly set to original path on failure
                }
            }
        }

        // Now, load the determined audio file
        string uri = new System.Uri(audioPathToLoad).AbsoluteUri;
        AudioType audioType = AudioType.WAV; // We only handle WAV for speed changes

        // The logic above ensures audioPathToLoad is either a cached file, a newly created file, or the original file.
        // If the original file is missing, the load process should have failed earlier.
        // We can remove the redundant File.Exists check here.

        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, audioType))
        {
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                if (clip == null)
                {
                    UnityEngine.Debug.LogError("DownloadHandlerAudioClip.GetContent returned null. Cannot load audio.");
                    main.SetLoadingText(false);
                    yield break;
                }
                
                audioSource.clip = clip;
                
                // Update the file's last access time to reflect it was just played
                try
                {
                    File.SetLastAccessTimeUtc(audioPathToLoad, System.DateTime.UtcNow);
                    UnityEngine.Debug.Log($"Updated LastAccessTime for: {Path.GetFileName(audioPathToLoad)}");
                }
                catch (System.Exception e)
                {
                    UnityEngine.Debug.LogWarning($"Failed to update LastAccessTime for {audioPathToLoad}: {e.Message}");
                }
                
                // Update the UI in Main.cs to reflect the applied speed
                if (main != null)
                {
                    main.UpdateSongSpeedUI(speedToApply);
                }
                
                if (noteHighway != null)
                {
                    noteHighway.psarcLoader = this;
                    noteHighway.LoadArrangementData(_arrangementData);
                    noteHighway.audioSource = audioSource;
                    audioSource.Play();
                    
                    // Notify Main script that the song has loaded and started playing
                    if (main != null)
                    {
                        main.SongLoadedAndStarted();
                    }
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

    /// <summary>
    /// Attempts to read the song name from the song_info.json file in the cache directory.
    /// </summary>
    /// <param name="cacheDir">The path to the song's cache directory.</param>
    /// <returns>The song name if found, otherwise the directory hash.</returns>
    private string GetSongNameFromCacheDir(string cacheDir)
    {
        string infoPath = Path.Combine(cacheDir, "song_info.json");
        if (File.Exists(infoPath))
        {
            try
            {
                string json = File.ReadAllText(infoPath);
                SongCacheInfo info = JsonUtility.FromJson<SongCacheInfo>(json);
                return info.SongName;
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogWarning($"Failed to read song info from {infoPath}: {e.Message}");
            }
        }
        // Fallback to the directory name (hash)
        return Path.GetFileName(cacheDir);
    }

    /// <summary>
    /// Cleans the audio cache by deleting individual audio files (both original and speed-adjusted)
    /// based on a Least Recently Used (LRU) policy until the total cache size is below the limit.
    /// Speed-adjusted files are prioritized for deletion over the original audio file.
    /// This is called when a new song or processed audio file is cached.
    /// </summary>
    /// <param name="fileToProtectPath">Optional path to a file that should not be deleted in this run (e.g., the file just created/loaded).</param>
    public void CleanSongCache(string fileToProtectPath = null)
    {
        // Convert MB limit to bytes using long to prevent overflow (up to 2TB)
        long limitBytes = (long)main.audioCacheLimitMB * 1024L * 1024L;
        string cacheRoot = Path.Combine(Application.persistentDataPath, "SongCache");

        if (!Directory.Exists(cacheRoot))
        {
            return;
        }

        // 1. Collect all cached audio files
        var allAudioFiles = new List<FileInfo>();
        var cacheDirectories = Directory.GetDirectories(cacheRoot);
        
        // Pre-fetch song names for logging
        var songNameMap = new Dictionary<string, string>();
        foreach (string dir in cacheDirectories)
        {
            songNameMap[dir] = GetSongNameFromCacheDir(dir);
            // Get all .wav files in the directory (no recursion needed)
            allAudioFiles.AddRange(Directory.GetFiles(dir, "*.wav").Select(f => new FileInfo(f)));
        }

        // 2. Create a list of all cache entries with access time, size, and song directory
        //    The protected file is included here so its size contributes to the total cache size.
        var allCacheEntries = allAudioFiles
            .Where(f => f.Exists) // Only consider existing files
            .Select(f =>
            {
                // Create a new FileInfo object from the path to ensure the file system properties are read fresh.
                var freshF = new FileInfo(f.FullName);

                return new
                {
                    Path = freshF.FullName,
                    Directory = Path.GetDirectoryName(freshF.FullName),
                    Size = freshF.Length,
                    // Use LastAccessTimeUtc as the "time last played"
                    LastAccessed = freshF.LastAccessTimeUtc, 
                    // IsOriginal is true for "audio.wav", false for "audio_XX.wav"
                    IsOriginal = Path.GetFileName(freshF.FullName).Equals("audio.wav", System.StringComparison.OrdinalIgnoreCase),
                    SongName = songNameMap[Path.GetDirectoryName(freshF.FullName)]
                };
            })
            .Where(c => c.Size > 0) // Filter out zero-length files
            .ToList();

        long currentSize = allCacheEntries.Sum(c => c.Size);
        UnityEngine.Debug.Log($"Audio Cache: Current Size = {currentSize / (1024.0 * 1024.0):F2} MB, Limit = {main.audioCacheLimitMB} MB.");

        if (currentSize <= limitBytes)
        {
            return; // Below limit, nothing to do
        }

        // The list of files to consider for deletion (excludes the file we just created/loaded)
        var deletableCacheEntries = allCacheEntries
            .Where(c => fileToProtectPath == null || !c.Path.Equals(fileToProtectPath, System.StringComparison.OrdinalIgnoreCase))
            .ToList();

        // 3. Group by directory to determine if an original file has speed-adjusted siblings,
        //    and then sort the files for deletion. The original audio file is protected from deletion
        //    as long as any speed-adjusted versions exist in its directory.
        var filesToDelete = deletableCacheEntries
            .GroupBy(c => c.Directory)
            .SelectMany(g =>
            {
                // Check if the song directory contains any speed-adjusted files, including the one being protected.
                // We use the unfiltered 'allAudioFiles' list for this check to correctly determine if the original file should be protected.
                string dir = g.Key;
                bool hasSpeedAdjustedSiblings = allAudioFiles
                    .Any(f => Path.GetDirectoryName(f.FullName).Equals(dir, System.StringComparison.OrdinalIgnoreCase) 
                           && !Path.GetFileName(f.FullName).Equals("audio.wav", System.StringComparison.OrdinalIgnoreCase));
                
                return g.Select(c => new
                {
                    c.Path,
                    c.Size,
                    c.LastAccessed,
                    c.IsOriginal,
                    c.SongName,
                    // IsProtectedOriginal is true if it's the original file AND there are speed-adjusted siblings.
                    // This file must be deleted last.
                    IsProtectedOriginal = c.IsOriginal && hasSpeedAdjustedSiblings
                });
            })
            // Sort order:
            // 1. Prioritize deletion of speed-adjusted files (IsOriginal = false) over original files (IsOriginal = true).
            .OrderBy(c => c.IsOriginal) 
            // 2. Oldest accessed first (LRU) as a tie-breaker among files of the same type (e.g., oldest stretched file goes first).
            .ThenBy(c => c.LastAccessed)        
            // 3. Protected Originals (true) are deleted last, ensuring the original is kept if possible.
            .ThenBy(c => c.IsProtectedOriginal)          
            .ToList();

        // 4. Iterate and Delete
        foreach (var file in filesToDelete)
        {
            if (currentSize <= limitBytes)
            {
                break;
            }

            try
            {
                File.Delete(file.Path);
                currentSize -= file.Size;
                UnityEngine.Debug.Log($"Cleaned cache: Deleted '{Path.GetFileName(file.Path)}' for song '{file.SongName}'. New size: {currentSize / (1024.0 * 1024.0):F2} MB.");
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError($"Failed to delete cache file {file.Path}: {e.Message}");
            }
        }

        UnityEngine.Debug.Log($"Audio Cache cleanup finished. Final size: {currentSize / (1024.0 * 1024.0):F2} MB.");
        
        // 5. Clean up empty song directories after file deletion
        foreach (string dir in cacheDirectories)
        {
            // Check if the directory is completely empty
            if (!Directory.EnumerateFileSystemEntries(dir).Any())
            {
                try
                {
                    Directory.Delete(dir);
                    UnityEngine.Debug.Log($"Cleaned cache: Deleted empty directory '{songNameMap[dir]}'.");
                }
                catch (System.Exception e)
                {
                    UnityEngine.Debug.LogWarning($"Failed to delete empty directory {dir}: {e.Message}");
                }
            }
        }
    }
}