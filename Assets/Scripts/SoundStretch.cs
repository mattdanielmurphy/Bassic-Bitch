
using UnityEngine;
using System.Diagnostics;
using System.IO;

public static class SoundStretch
{
    public static string Process(string inputPath, float tempo)
    {
        string toolDir = Path.Combine(Application.streamingAssetsPath, "tools");
        string cliPath = "";

#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        cliPath = Path.Combine(Application.dataPath, "Plugins/soundstretch/mac/SoundStretch");
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        cliPath = Path.Combine(Application.dataPath, "Plugins/soundstretch/windows/soundstretch.exe");
#elif UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
        cliPath = Path.Combine(Application.dataPath, "Plugins/soundstretch/linux/soundstretch");
#else
        UnityEngine.Debug.LogError("Unsupported platform for SoundStretch.");
        return null;
#endif

        if (!File.Exists(cliPath))
        {
            UnityEngine.Debug.LogError($"SoundStretch not found at: {cliPath}.");
            return null;
        }

        // On macOS/Linux, ensure the executable has execute permissions
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
                UnityEngine.Debug.LogError($"Failed to set execute permissions on SoundStretch. STDERR: {chmodProcess.StandardError.ReadToEnd()}");
                return null;
            }
        }

        string outputPath = Path.Combine(Path.GetDirectoryName(inputPath), Path.GetFileNameWithoutExtension(inputPath) + "_stretched.wav");

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
}
