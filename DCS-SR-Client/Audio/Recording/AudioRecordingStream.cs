using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Recording
{
    // abstract base class for a "stream" of audio information. the stream may return samples
    // from the audio engine or construct samples from other streams (ie, mix).
    internal abstract class AudioRecordingStream
    {
        // return a tag string that identifies the stream.
        public string Tag { get; set; }

        // return the number of samples available in the stream.
        abstract public int Count();

        // read up to maxSampleCount samples from the stream and return them in the provided
        // buffer. returns the number of samples written to the buffer;
        abstract public int Read(float[] samples, int maxSampleCount);
    }
}
