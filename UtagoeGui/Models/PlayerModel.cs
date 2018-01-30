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

            this._timer = new Timer(_ =>
            {
                if (!this._isDisposed)
                    this.SetPositionToStore();
            });

            this._player.PlaybackStopped += (sender, e) =>
            {
                if (this._isDisposed) return;

                if (this._stoppingPlaybackManually)
                {
                    this._stoppingPlaybackManually = false;
                }
                else
                {
                    this.StopTimer();
                    this._store.PlaybackPositionInSamples = 0;
                }
            };
        }

        public void TogglePlaybackState()
        {
            if (this._player.PlaybackState == PlaybackState.Playing)
            {
                this.Pause();
            }
            else
            {
                this.ResumeFromPlaybackPosition();
            }
        }

        public void Stop()
        {
            if (this._player.PlaybackState == PlaybackState.Playing)
                this.Pause();
        }

        public void MovePlaybackPosition(double positionInSamples)
        {
            this._store.PlaybackPositionInSamples = positionInSamples;
        }

        public void Dispose()
        {
            if (this._isDisposed) return;
            this._isDisposed = true;

            this._store.IsPlaying = false;

            this._timer.Dispose();
            this._player.Dispose();
            this._waveStream.Dispose();
        }

        private void StartTimer()
        {
            this._store.IsPlaying = true;
            this._timer.Change(TimeSpan.Zero, s_timerInterval);
        }

        private void StopTimer()
        {
            this._store.IsPlaying = false;
            this._timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private void SetPositionToStore()
        {
            this._store.PlaybackPositionInSamples = (this._playbackStartPosition + this._player.GetPosition())
                / (double)this._waveStream.WaveFormat.BlockAlign;
        }

        private void Pause()
        {
            this._player.Pause();
            this.StopTimer();
            this.SetPositionToStore();
        }

        private void ResumeFromPlaybackPosition()
        {
            if (this._player.PlaybackState != PlaybackState.Stopped)
            {
                this._stoppingPlaybackManually = true;
                this._player.Stop();
            }

            this._playbackStartPosition = (long)(this._waveStream.BlockAlign * this._store.PlaybackPositionInSamples);
            this._waveStream.Position = this._playbackStartPosition;
            this._player.Init(this._waveStream);
            this._player.Play();
            this.StartTimer();
        }
    }
}
