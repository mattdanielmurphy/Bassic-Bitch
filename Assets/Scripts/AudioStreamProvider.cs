using UnityEngine;
using System.Collections;

public class AudioStreamProvider : MonoBehaviour
{
    private AudioStreamer _audioStreamer;
    private AudioSource _audioSource;
    private long _samplesPlayed = 0;
    private int _sampleRate = 44100;
    private int _channels = 2;

    private float[] _fullClipData;
    private long _fullClipSamplesPlayed = 0;
    private volatile bool _isActuallyPlaying = false; // Flag to track playback state on main thread, accessible by audio thread.

    public float StreamedTime => (float)_samplesPlayed / (_sampleRate * _channels);
    public bool IsPlaying => _isActuallyPlaying; // Use our thread-safe flag.

    public bool IsStreaming()
    {
        return _audioStreamer != null;
    }

    private void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
        }
        _sampleRate = AudioSettings.outputSampleRate; // Use the actual output sample rate
        
        // This component drives the audio playback, so it needs a clip to work with.
        _audioSource.clip = AudioClip.Create("Streaming", 4096, _channels, _sampleRate, true, OnAudioRead);
        _audioSource.loop = true;
    }

    void OnAudioRead(float[] data)
    {
        OnAudioFilterRead(data, _channels);
    }

    public void SetFullClipData(float[] data, float startTime)
    {
        _fullClipData = data;
        _fullClipSamplesPlayed = (long)(startTime * _sampleRate * _channels);
        _samplesPlayed = _fullClipSamplesPlayed;

        if (_audioStreamer != null)
        {
            _audioStreamer.StopStreaming();
            _audioStreamer = null;
        }

        if (!_isActuallyPlaying)
        {
            _audioSource.Play();
            _isActuallyPlaying = true;
        }
    }

    public void SetStreamer(AudioStreamer streamer)
    {
        _audioStreamer = streamer;
        _fullClipData = null;
        _samplesPlayed = 0; // Reset time when a new stream is set
    }

    public void Reset()
    {
        _samplesPlayed = 0;
        _fullClipSamplesPlayed = 0;
    }

    public void RestartStreamingAtTime(float time)
    {
        if (_audioStreamer != null)
        {
            // Reset the internal sample counter before the restart call
            _samplesPlayed = 0;
            _audioStreamer.RestartStreaming(time);
        }
    }

    public void Play(string path, float tempo)
    {
        _fullClipData = null;
        _audioStreamer = new AudioStreamer(tempo);
        _audioStreamer.StartStreaming(path);
        _samplesPlayed = 0; // Reset time on new play
        StartCoroutine(WaitForBuffer());
    }

    public void PlayStream(string path, float tempo, float startTime)
    {
        if (_audioStreamer != null)
        {
            _audioStreamer.StopStreaming();
        }

        _fullClipData = null;
        _audioStreamer = new AudioStreamer(tempo);
        _audioStreamer.StartStreaming(path, startTime);
        _samplesPlayed = 0;
        StartCoroutine(WaitForBuffer());
    }

    public void Unpause()
    {
        if (_isActuallyPlaying) return;

        _audioSource.UnPause();
        _isActuallyPlaying = true;
    }

    public void Pause()
    {
        if (!_isActuallyPlaying) return;

        _audioSource.Pause();
        _isActuallyPlaying = false;
    }

    public void StopStreamerOnly()
    {
        if (_audioStreamer != null)
        {
            _audioStreamer.StopStreaming();
            _audioStreamer = null;
        }
    }

    public void Stop()
    {
        if (_audioStreamer != null)
        {
            _audioStreamer.StopStreaming();
            _audioStreamer = null;
        }
        _fullClipData = null;
        _audioSource.Stop();
        _isActuallyPlaying = false;
    }

    private void OnDestroy()
    {
        Stop();
    }

    private IEnumerator WaitForBuffer()
    {
        while (_audioStreamer != null && !_audioStreamer.BufferReady)
        {
            yield return null;
        }

        if (_audioStreamer != null && !_isActuallyPlaying)
        {
            _audioSource.Play();
            _isActuallyPlaying = true;
        }
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        if (!_isActuallyPlaying)
        {
            // If the AudioSource is paused/stopped, fill with silence.
            // This is necessary because the audio thread may continue to run
            // and call this function even when the source is paused,
            // especially if the custom clip is set to loop.
            System.Array.Clear(data, 0, data.Length);
            return;
        }

        if (_fullClipData != null)
        {
            int dataLen = data.Length;
            long remainingSamples = _fullClipData.Length - _fullClipSamplesPlayed;
            int samplesToCopy = (int)Mathf.Min(dataLen, remainingSamples);

            if (samplesToCopy > 0)
            {
                System.Array.Copy(_fullClipData, _fullClipSamplesPlayed, data, 0, samplesToCopy);
                _fullClipSamplesPlayed += samplesToCopy;
            }

            // Fill the rest with silence if we've reached the end
            for (int i = samplesToCopy; i < dataLen; i++)
            {
                data[i] = 0;
            }

            _samplesPlayed = _fullClipSamplesPlayed;
            return;
        }

        if (_audioStreamer == null || !_audioStreamer.BufferReady)
        {
            // Fill with silence if the buffer isn't ready
            System.Array.Clear(data, 0, data.Length);
            return;
        }

        byte[] byteBuffer = new byte[data.Length * 2];
        int bytesRead = _audioStreamer.Read(byteBuffer, 0, byteBuffer.Length);

        int samplesRead = bytesRead / 2;

        for (int i = 0; i < data.Length; i++)
        {
            if (i < samplesRead)
            {
                short sample = (short)(byteBuffer[i * 2] | byteBuffer[i * 2 + 1] << 8);
                data[i] = sample / 32768f;
            }
            else
            {
                data[i] = 0; // Fill with silence if we run out of data
            }
        }
        
        _samplesPlayed += samplesRead;
    }
}
