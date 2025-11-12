using UnityEngine;
using System.IO;
using System.Collections;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using Rocksmith2014PsarcLib.Psarc;
using UnityEngine.Networking;

public class PsarcLoader : MonoBehaviour
{
    public string psarcFilePath = "/Users/matthewmurphy/Library/CloudStorage/CloudMounter-MattMurphy/My Documents/Bass/4 String/Tier 1 - Foundational (Easiest)/The Beatles - When I'm Sixty Four (B)_p - Tier 1.psarc";
    public NoteHighway noteHighway; // Drag the NoteHighway GameObject here in the Inspector
    public bool startMuted = false; // Option to start the audio muted
    public AudioSource audioSource;

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

        if (!File.Exists(psarcFilePath))
        {
            UnityEngine.Debug.LogError("PSARC file does not exist: " + psarcFilePath);
            return;
        }

        try
        {
            using (var psarc = new PsarcFile(psarcFilePath))
            {
                UnityEngine.Debug.Log("Opened PSARC file: " + psarcFilePath);

                // UnityEngine.Debug.Log("Files in PSARC:");
                // foreach (var entry in psarc.TOC.Entries)
                // {
                //     UnityEngine.Debug.Log($"- {entry.Path}");
                // }

                // The actual audio file name is likely different from "song.ogg".
                // We will look for a file ending in .ogg or .wem.
                var audioEntry = psarc.TOC.Entries.FirstOrDefault(e => e.Path != null && (e.Path.EndsWith(".ogg") || e.Path.EndsWith(".wem")));

                if (audioEntry != null)
                {
                    string audioName = Path.GetFileName(audioEntry.Path);
                    string tempPath = Path.Combine(Application.temporaryCachePath, audioName);
                    
                    // UnityEngine.Debug.Log($"Attempting to extract audio file: {audioEntry.Path}");

                    using (var outFile = new FileStream(tempPath, FileMode.Create))
                    {
                        psarc.InflateEntry(audioEntry, outFile);
                    }
                    // UnityEngine.Debug.Log($"Extracted audio: {audioName} to {tempPath}");

                    string fileToLoadPath = tempPath;
                    string extension = Path.GetExtension(tempPath).ToLower();

                    if (extension == ".wem")
                    {
                        // If it's a WEM, check for a converted WAV file in the same location
                        string wavPath = Path.ChangeExtension(tempPath, ".wav");
                        if (File.Exists(wavPath))
                        {
                            // UnityEngine.Debug.Log($"Found converted WAV file: {wavPath}. Loading it instead of WEM.");
                            fileToLoadPath = wavPath;
                        }
                        else
                        {
                            // Convert the WEM to WAV
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

                    // Play the audio file
                    StartCoroutine(LoadAndPlayAudio(fileToLoadPath));
                }
                else
                {
                    UnityEngine.Debug.LogError("No audio file (.ogg or .wem) found in PSARC.");
                }

                // Find Arrangement Entry (contains the chart)
                var arrangementEntry = psarc.TOC.Entries.FirstOrDefault(e => e.Path != null && e.Path.EndsWith("_bass.xml"));
                if (arrangementEntry != null) {
                    string arrangementTempPath = Path.Combine(
                        Application.temporaryCachePath, Path.GetFileName(arrangementEntry.Path));
                    using (var outFile = new FileStream(arrangementTempPath, FileMode.Create)) {
                        psarc.InflateEntry(arrangementEntry, outFile);
                    }
                    // UnityEngine.Debug.Log($"Extracted arrangement XML: {arrangementEntry.Path} to {arrangementTempPath}");

                    List<NoteData> notes = ArrangementParser.ParseArrangement(arrangementTempPath);
                    
                    // Pass notes to the NoteHighway component
                    if (noteHighway != null)
                    {
                        noteHighway.notes = notes;
                        noteHighway.audioSource = audioSource;
                    }
                    else
                    {
                        UnityEngine.Debug.LogError("NoteHighway not set in Inspector!");
                    }
                }
                else {
                    UnityEngine.Debug.LogError("Bass arrangement XML not found in PSARC!");
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

    IEnumerator LoadAndPlayAudio(string filePath)
    {
        // Use the Uri class to correctly format the file path into a URI string.
        // This is the most reliable way to handle local file paths for UnityWebRequest.
        string uri = new System.Uri(filePath).AbsoluteUri;
        string extension = Path.GetExtension(filePath).ToLower();
        AudioType audioType = AudioType.UNKNOWN;

        if (extension == ".wav")
        {
            audioType = AudioType.WAV;
        }

        // --- Debugging Checks ---
        if (!File.Exists(filePath))
        {
            UnityEngine.Debug.LogError($"Audio file not found at path: {filePath}");
            yield break;
        }

        FileInfo fileInfo = new FileInfo(filePath);
        if (fileInfo.Length == 0)
        {
            UnityEngine.Debug.LogError($"Audio file is empty (0 bytes) at path: {filePath}");
            yield break;
        }
        
        // UnityEngine.Debug.Log($"Attempting to load audio from URI: {uri} (File size: {fileInfo.Length} bytes)");
        // --- End Debugging Checks ---

        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, audioType))
        {
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                if (clip == null)
                {
                    UnityEngine.Debug.LogError("DownloadHandlerAudioClip.GetContent returned null. This is often the cause of the FMOD error.");
                    yield break;
                }
                audioSource.clip = clip;
                audioSource.Play();
                // UnityEngine.Debug.Log("Playing audio: " + filePath);
            }
            else
            {
                UnityEngine.Debug.LogError("Error loading audio: " + www.error);
            }
        }
    }
}
