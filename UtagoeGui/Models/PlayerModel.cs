using NAudio.Wave;
using System;
using System.Threading;

namespace UtagoeGui.Models
{
    internal class PlayerModel : IDisposable
    {
        private static readonly TimeSpan s_timerInterval = TimeSpan.FromMilliseconds(1000.0 / 30.0); // 30回/s

        private readonly AppStore _store;
        private bool _isDisposed;

        private readonly WaveStream _waveStream;
        private readonly WaveOutEvent _player = new WaveOutEvent();
        private readonly Timer _timer;
        private long _playbackStartPosition;
        private bool _stoppingPlaybackManually;

        public PlayerModel(WaveStream waveStream, AppStore writableStore)
        {
            this._store = writableStore;
            this._waveStream = waveStream;
            this._timer = new Timer(_ => this.SetPositionToStore());
        }

        public void TogglePlaybackState()
        {
            if (this._player.PlaybackState == PlaybackState.Playing)
            {
                this.Pause();
            }
            else
            {
                this.PlayFromPosition(this._store.PlaybackPositionInSamples);
            }
        }

        public void Stop()
        {
            // TODO
        }

        public void MovePlaybackPosition(double positionInSamples)
        {
            // TODO
        }

        public void Dispose()
        {
            if (this._isDisposed) return;
            this._isDisposed = true;

            this._timer.Dispose();
            this._stoppingPlaybackManually = true;
            this._player.Dispose();
            this._waveStream.Dispose();
        }

        private void StartTimer()
        {
            this._timer.Change(TimeSpan.Zero, s_timerInterval);
        }

        private void StopTimer()
        {
            this._timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private void SetPositionToStore()
        {
            // TODO
        }

        private void Pause()
        {
            this._player.Pause();
            this.StopTimer();
            this.SetPositionToStore();
        }

        private void PlayFromPosition(double pos)
        {
            if (this._player.PlaybackState != PlaybackState.Stopped)
            {
                this._stoppingPlaybackManually = true;
                this._player.Stop();
            }

            this._playbackStartPosition = (long)(this._waveStream.BlockAlign * pos);
            this._waveStream.Position = this._playbackStartPosition;
            this._player.Init(this._waveStream);
            this._player.Play();
            this.StartTimer();
        }
    }
}
