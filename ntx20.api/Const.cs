using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ntx20.api
{
    public static class Const
    {
        public const string i_audio_format = "audio-format";
        public const string i_audio_channel = "audio-channel";
        public const string features = "features";
        public const string lexicon = "lexicon";

    }
    public static class AudioDecoder
    {
        public static readonly string[] pcmFormats = { "s16le", "alaw", "mulaw" };
        public static readonly string[] sampleFormats = { "i2" };
        public static readonly string[] sampleRates = { "8000", "16000", "32000", "48000", "96000", "11025", "22050", "44100" };
        public static readonly string[] channelLayouts = { "mono", "stereo" };
        public static readonly string[] channelSelects = { "downmix", "left", "right" };
    }

    public static class Constants
    {
        public const string DefaultTextSplit = @"(?<=[\s>\.\,\:\?\!\-]+)|(?=[\s<\.\,\:\?\!\-]+)";
        public const string DefaultPlusMatch = @"^[\.\,\:\?\!\s\-]+$";
    }
}
