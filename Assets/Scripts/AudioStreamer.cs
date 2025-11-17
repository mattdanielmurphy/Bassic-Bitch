using UnityEngine;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Collections.Concurrent;

public class AudioStreamer
{
    private Process soundStretchProcess;
    private Thread streamingThread;
    private Thread inputThread;
    private Thread errorLoggingThread;
    private ConcurrentQueue<byte> circularBuffer;
    private readonly object _lock = new object();
    private bool isStreaming = false;

    private const int BufferSize = 44100 * 2 * 4; // 4 seconds of audio at 44.1kHz, 16-bit stereo

    public bool BufferReady { get; private set; } = false;

    private float _tempo;
    private float _startTime;
    private string _inputPath;

    public AudioStreamer(float tempo)
    {
        _tempo = tempo;
        circularBuffer = new ConcurrentQueue<byte>();
    }

    public void StartStreaming(string inputPath, float startTime = 0f)
    {
        _startTime = startTime;
        _inputPath = inputPath;
        soundStretchProcess = SoundStretch.ProcessStream(_tempo);

        if (soundStretchProcess == null)
        {
            UnityEngine.Debug.LogError("Cannot start streaming, the SoundStretch process failed to initialize.");
            return;
        }

        isStreaming = true;
        inputThread = new Thread(() => WriteToProcessInput(_inputPath, _startTime));
        inputThread.Start();
        streamingThread = new Thread(() => ReadFromProcessOutput());
        streamingThread.Start();
        errorLoggingThread = new Thread(() => LogErrors());
        errorLoggingThread.Start();
    }

    public void RestartStreaming(float newStartTime)
    {
        // 1. Stop the current stream and kill the process
        StopStreaming();

        // 2. Clear the buffer of old data
        circularBuffer = new ConcurrentQueue<byte>();
        BufferReady = false;

        // 3. Start a new stream from the new time
        StartStreaming(_inputPath, newStartTime);
    }

    public void StopStreaming()
    {
        lock (_lock)
        {
            if (!isStreaming) return;
            isStreaming = false;

            // Wait for threads to finish, but with a timeout to prevent freezing
            if (inputThread != null && inputThread.IsAlive)
            {
                inputThread.Join(500);
            }
            
            if (streamingThread != null && streamingThread.IsAlive)
            {
                streamingThread.Join(500); // 500ms timeout
            }

            if (errorLoggingThread != null && errorLoggingThread.IsAlive)
            {
                errorLoggingThread.Join(500);
            }

            if (soundStretchProcess != null && !soundStretchProcess.HasExited)
            {
                try
                {
                    // Try to shut down gracefully first
                    soundStretchProcess.StandardInput.Close();

                    // Wait a moment for the process to exit on its own
                    if (!soundStretchProcess.WaitForExit(1000)) // 1 second timeout
                    {
                        // If it's still running, then kill it
                        UnityEngine.Debug.LogWarning("SoundStretch process did not exit gracefully. Killing.");
                        soundStretchProcess.Kill();
                    }
                }
                catch (System.Exception e)
                {
                    UnityEngine.Debug.LogWarning($"Exception during SoundStretch shutdown: {e.Message}");
                    // Fallback to killing if other methods fail
                    if (!soundStretchProcess.HasExited)
                    {
                        soundStretchProcess.Kill();
                    }
                }
            }
        }
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        int bytesRead = 0;
        for (int i = 0; i < count; i++)
        {
            if (circularBuffer.TryDequeue(out byte result))
            {
                buffer[offset + i] = result;
                bytesRead++;
            }
            else
            {
                break;
            }
        }
        return bytesRead;
    }

    private void WriteToProcessInput(string inputPath, float startTime)
    {
        try
        {
            using (FileStream inputFile = new FileStream(inputPath, FileMode.Open, FileAccess.Read))
            using (Stream processInput = soundStretchProcess.StandardInput.BaseStream)
            {
                // First, write the 44-byte WAV header regardless of start time
                byte[] header = new byte[44];
                inputFile.Read(header, 0, 44);
                processInput.Write(header, 0, 44);

                // Now, calculate and seek to the start time if necessary
                if (startTime > 0)
                {
                    int sampleRate = System.BitConverter.ToInt32(header, 24);
                    int channels = System.BitConverter.ToInt16(header, 22);
                    int bitsPerSample = System.BitConverter.ToInt16(header, 34);
                    long byteOffset = (long)(startTime * sampleRate * channels * (bitsPerSample / 8));
                    
                    // The offset must be relative to the start of the data chunk (after the 44-byte header)
                    inputFile.Seek(44 + byteOffset, SeekOrigin.Begin);
                }
                
                byte[] buffer = new byte[8192]; // Use a reasonably sized buffer
                int bytesRead;
                while (isStreaming && (bytesRead = inputFile.Read(buffer, 0, buffer.Length)) > 0)
                {
                    processInput.Write(buffer, 0, bytesRead);
                }
            }
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError($"Error writing to SoundStretch input: {e.Message}");
        }
        finally
        {
            // This is critical: closing the input stream signals EOF to the process.
            if (soundStretchProcess != null && !soundStretchProcess.HasExited)
            {
                try
                {
                    soundStretchProcess.StandardInput.Close();
                }
                catch (System.Exception)
                {
                    // Ignore errors if the process has already exited.
                }
            }
        }
    }

    private void ReadFromProcessOutput()
    {
        try
        {
            using (Stream processOutput = soundStretchProcess.StandardOutput.BaseStream)
            {
                byte[] buffer = new byte[4096];
                while (isStreaming)
                {
                    int bytesRead = processOutput.Read(buffer, 0, buffer.Length);
                    if (bytesRead <= 0)
                    {
                        // End of stream, break the loop.
                        break;
                    }

                    for (int i = 0; i < bytesRead; i++)
                    {
                        circularBuffer.Enqueue(buffer[i]);
                    }

                    if (!BufferReady && circularBuffer.Count >= BufferSize / 2)
                    {
                        BufferReady = true;
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError($"Error reading from SoundStretch output: {e.Message}");
        }
        finally
        {
            isStreaming = false;
        }
    }

    private void LogErrors()
    {
        if (soundStretchProcess == null) return;

        try
        {
            using (var reader = soundStretchProcess.StandardError)
            {
                while (isStreaming && !soundStretchProcess.HasExited)
                {
                    string line = reader.ReadLine();
                    if (!string.IsNullOrEmpty(line))
                    {
                        UnityEngine.Debug.LogError($"SoundStretch STDERR: {line}");
                    }
                    Thread.Sleep(10); // Prevent tight looping
                }
            }
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogWarning($"Exception in LogErrors thread: {e.Message}");
        }
    }
}
