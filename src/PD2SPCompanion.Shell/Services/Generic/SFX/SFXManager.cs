using PD2SPCompanion.Shell.Services.Settings;
using NAudio.Wave;
using NAudio;
using System;
using System.Collections.Generic;
using System.IO;

namespace PD2SPCompanion.Shell.Services.Generic;

/// <summary>
/// Plays the UI sound effects. One pre-initialised output device per sound, so a click
/// costs a seek and a play rather than opening a device.
/// <para>
/// The sounds are the game's own, read from its sound archive in the player's install - not shipped
/// with the editor: the click is <c>button.wav</c> in <c>d2sfx.mpq</c>,
/// the file the editor once carried a copy of, byte for byte. With no game folder yet - the first
/// run's setup - nothing plays.
/// </para>
/// </summary>
public class SFXManager : ISFXManager, IDisposable
{
    #region Child Class
    private sealed class SFX_Player : IDisposable
    {
        private WaveOutEvent? _device;
        private WaveFileReader? _fileStream;
        private bool _device_isInitilized;

        public SFX_Player(byte[] wav)
        {
            // Held in memory: a few kilobytes, and it has to be seekable to replay.
            var buffer = new MemoryStream(wav, writable: false);

            // The assets are plain PCM wav, so the wav reader is enough. The 2023 version
            // used StreamMediaFoundationReader, which pulls in Media Foundation to decode
            // a format that needs no decoding.
            _fileStream = new WaveFileReader(buffer);

            //Init
            _device = new WaveOutEvent();
            TryInitDevice();
        }

        /// <summary>
        /// A machine with no output device throws <see cref="MmResult.BadDeviceId"/>. That is
        /// not worth failing startup over, so it is retried on the next play instead.
        /// </summary>
        private void TryInitDevice()
        {
            try
            {
                _device!.Init(_fileStream);
                _device_isInitilized = true;
            }
            catch (MmException ex) when (ex.Result == MmResult.BadDeviceId)
            {
                _device_isInitilized = false;
            }
        }

        /// <summary>
        /// The sound asked for, and played off the caller's thread: stopping a sound that is still
        /// playing waits on the device - some 20 ms, measured on the window's thread where two clicks follow
        /// each other. One worker at a time; a press that arrives while one is waiting to start is the same press.
        /// </summary>
        public void Play()
        {
            if (System.Threading.Interlocked.Exchange(ref _waiting, 1) == 1)
                return;

            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                lock (_gate)
                {
                    System.Threading.Interlocked.Exchange(ref _waiting, 0);
                    PlayNow();
                }
            });
        }

        private int _waiting;
        private readonly object _gate = new();

        private void PlayNow()
        {
            if (_device is null || _fileStream is null)
                return;

            try
            {
                // One click at a time, several fired together never cutting
                // in on the one playing: one asked for while one sounds is that one - it neither
                // restarts it, as it did, nor follows it. The page changes the program makes by itself ask for none
                // (MainViewModel's Switch* and Go*); this keeps any other from stuttering.
                if (_device.PlaybackState == PlaybackState.Playing)
                    return;

                _device.Stop();
                if (!_device_isInitilized)
                    TryInitDevice();
                if (!_device_isInitilized)
                    return;

                _fileStream.Position = 0;
                _device.Play();
            }
            catch (MmException ex) when (ex.Result == MmResult.BadDeviceId)
            {
                _device_isInitilized = false;
            }
            catch (Exception problem)
            {
                // On a worker now: nothing of a sound's may end the program.
                PD2SPCompanion.Core.Diagnostics.Log.Caught(problem);
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _device?.Dispose();
                _device = null;
                _device_isInitilized = false;
                _fileStream?.Dispose();
                _fileStream = null;
            }
        }
    }
    #endregion

    /// <summary>The game's sound archive, beside its other archives.</summary>
    private const string SoundArchive = "d2sfx.mpq";

    /// <summary>The menu click, as the game keeps it.</summary>
    private const string Click = @"data\global\sfx\cursor\button.wav";

    private readonly EditorSettings _settings;
    private SFX_Player? _click;
    private bool _readFrom;

    public SFXManager(EditorSettings settings) => _settings = settings;

    /// <summary>
    /// The sounds read, ahead of the first click - behind the splash, with the game's tables (App.WarmUp).
    /// Once a folder is set; asked again later where there was none yet.
    /// </summary>
    public void Prepare()
    {
        if (_readFrom || !_settings.HasGameFolder)
            return;
        _readFrom = true;

        try
        {
            using var archive = PD2SPCompanion.Core.Mpq.MpqArchive.Open(Path.Combine(_settings.GameFolder, SoundArchive));
            if (archive.ReadFile(Click) is { Length: > 0 } wav)
                _click = new SFX_Player(wav);
        }
        catch (Exception problem)
        {
            // No click is no loss: the editor works the same in silence.
            PD2SPCompanion.Core.Diagnostics.Log.Caught(problem);
        }
    }

    /// <summary>How many clicks were asked for since start: what the tests count (one press, one click).</summary>
    public int Presses { get; private set; }

    /// <summary>How many of those were sounded: all of them while Sounds is on, none while it is off.</summary>
    public int Sounded { get; private set; }

    public void Method_ButtonSFX_Press()
    {
        Presses++;
        if (!_settings.Sounds)
            return;

        Sounded++;
        Prepare();
        _click?.Play();
    }

    public void Dispose()
    {
        _click?.Dispose();
        _click = null;
    }
}
