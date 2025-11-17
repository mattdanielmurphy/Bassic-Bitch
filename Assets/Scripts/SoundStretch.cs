using UnityEngine;
using System.Diagnostics;
using System.IO;

public static class SoundStretch
{
    public static Process ProcessStream(float tempo)
    {
        string cliPath = GetCliPath();
        if (cliPath == null)
        {
            return null;
        }

        // On macOS/Linux, ensure the executable has execute permissions
        if (Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor)
        {
            if (!EnsureExecutePermissions(cliPath))
            {
                return null;
            }
        }

        Process process = new Process();
        process.StartInfo.FileName = cliPath;
        process.StartInfo.Arguments = $"stdin stdout -tempo={tempo}";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;

        UnityEngine.Debug.Log($"SoundStretch: Tempo: {tempo}");
        UnityEngine.Debug.Log($"SoundStretch: CLI Path: {cliPath}");
        UnityEngine.Debug.Log($"SoundStretch: Arguments: {process.StartInfo.Arguments}");

        try
        {
            process.Start();
            return process;
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError($"Error starting SoundStretch process: {e.Message}");
            return null;
        }
    }
    
    public static string ProcessFile(string inputPath, string outputPath, float tempo)
    {
        string cliPath = GetCliPath();

        if (cliPath == null) {
            return null;
        }
        
        // On macOS/Linux, ensure the executable has execute permissions
        if (Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor)
        {
            if (!EnsureExecutePermissions(cliPath)) {
                return null;
            }
        }

        Process process = new Process();
        process.StartInfo.FileName = cliPath;
        process.StartInfo.Arguments = $"\"{inputPath.Replace("\"", "\\\"")}\" \"{outputPath.Replace("\"", "\\\"")}\" -tempo={tempo}";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;

        UnityEngine.Debug.Log($"SoundStretch: Input Path: {inputPath}");
        UnityEngine.Debug.Log($"SoundStretch: Output Path: {outputPath}");
        UnityEngine.Debug.Log($"SoundStretch: Tempo: {tempo}");
        UnityEngine.Debug.Log($"SoundStretch: CLI Path: {cliPath}");
        UnityEngine.Debug.Log($"SoundStretch: Arguments: {process.StartInfo.Arguments}");

        try
        {
            process.Start();
            process.WaitForExit();

            string stdOut = process.StandardOutput.ReadToEnd();
            string stdErr = process.StandardError.ReadToEnd();

            UnityEngine.Debug.Log($"SoundStretch STDOUT: {stdOut}");
            UnityEngine.Debug.Log($"SoundStretch STDERR: {stdErr}");

            if (process.ExitCode == 0)
            {
                UnityEngine.Debug.Log($"SoundStretch: Successfully processed audio to {outputPath}");
                return outputPath;
            }
            else
            {
                UnityEngine.Debug.LogError($"SoundStretch failed with exit code {process.ExitCode}.\nSTDERR: {stdErr}");
                return null;
            }
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError($"Error starting SoundStretch process: {e.Message}");
            return null;
        }
    }

    private static string GetCliPath()
    {
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        string cliPath = Path.Combine(Application.dataPath, "Plugins/soundstretch/mac/SoundStretch");
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        string cliPath = Path.Combine(Application.dataPath, "Plugins/soundstretch/windows/soundstretch.exe");
#elif UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
        string cliPath = Path.Combine(Application.dataPath, "Plugins/soundstretch/linux/soundstretch");
#else
        UnityEngine.Debug.LogError("Unsupported platform for SoundStretch.");
        return null;
#endif

        if (!File.Exists(cliPath))
        {
            UnityEngine.Debug.LogError($"SoundStretch not found at: {cliPath}.");
            return null;
        }

        return cliPath;
    }

    private static bool EnsureExecutePermissions(string cliPath)
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
            UnityEngine.Debug.LogError($"Failed to set execute permissions on SoundStretch. STDERR: {chmodProcess.StandardError.ReadToEnd()}");
            return false;
        }

        return true;
    }
}