using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AutoHarmony.Models
{
    public abstract class RecoderProvider
    {
        public string DisplayName { get; protected set; }

        public abstract IWaveIn CreateWaveIn();

        public override string ToString() => this.DisplayName;
    }

    public class DefaultCaptureDeviceRecoderProvider : RecoderProvider
    {
        public static DefaultCaptureDeviceRecoderProvider Default { get; } = new DefaultCaptureDeviceRecoderProvider();

        public DefaultCaptureDeviceRecoderProvider()
        {
            this.DisplayName = "既定の録音デバイス";
        }

        public override IWaveIn CreateWaveIn()
        {
            return new WasapiCapture(WasapiCapture.GetDefaultCaptureDevice(), true, 50);
        }
    }

    public class CaptureDeviceRecoderProvider : RecoderProvider
    {
        public MMDevice Device { get; }

        public CaptureDeviceRecoderProvider(MMDevice device)
        {
            this.Device = device;
            this.DisplayName = device.FriendlyName;
        }

        public override IWaveIn CreateWaveIn()
        {
            return new WasapiCapture(this.Device, true, 50);
        }
    }

    public class DefaultRenderDeviceRecoderProvider : RecoderProvider
    {
        public static DefaultRenderDeviceRecoderProvider Default { get; } = new DefaultRenderDeviceRecoderProvider();

        public DefaultRenderDeviceRecoderProvider()
        {
            this.DisplayName = "既定の再生デバイス";
        }

        public override IWaveIn CreateWaveIn()
        {
            return new WasapiLoopbackCapture();
        }
    }

    public class RenderDeviceRecoderProvider : RecoderProvider
    {
        public MMDevice Device { get; }

        public RenderDeviceRecoderProvider(MMDevice device)
        {
            this.Device = device;
            this.DisplayName = device.FriendlyName;
        }

        public override IWaveIn CreateWaveIn()
        {
            return new WasapiLoopbackCapture(this.Device);
        }
    }
}
