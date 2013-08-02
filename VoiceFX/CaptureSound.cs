using System;
using System.Linq;
using SlimDX;
using SlimDX.DirectSound;
using SlimDX.Multimedia;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MultithreadedStream;
using System.Numerics;

namespace VoiceFX
{
    class Program
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        static extern short GetAsyncKeyState(int key);
        const int VK_ESCAPE = 0x1B;

        static void Main(string[] args)
        {
            #region Capture
            WaveFormat format = new WaveFormat();
            format.FormatTag = WaveFormatTag.Pcm;
            format.Channels = 1;
            format.BitsPerSample = 16;
            format.SamplesPerSecond = 32768;
            format.BlockAlignment = (short)(format.BitsPerSample / 8);
            format.AverageBytesPerSecond = format.SamplesPerSecond * format.BlockAlignment * format.Channels;

            //FileStream stream = File.Open("output.raw", FileMode.OpenOrCreate);
            MemoryStreamMultiplexer stream = new MemoryStreamMultiplexer();

            SoundBufferDescription primaryBufferDescription = new SoundBufferDescription();
            primaryBufferDescription.Format = format;
            primaryBufferDescription.Flags = BufferFlags.GlobalFocus;
            primaryBufferDescription.SizeInBytes = 2 * format.AverageBytesPerSecond;

            // Primary Buffer
            DirectSound ds = new DirectSound();//DirectSoundGuid.DefaultVoiceCaptureDevice);
            ds.SetCooperativeLevel(System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle, CooperativeLevel.Normal);

            // Capture
            CaptureBufferDescription captureBufferDescription = new CaptureBufferDescription()
            {
                Format = format,
                BufferBytes = format.AverageBytesPerSecond,
                WaveMapped = true,
                ControlEffects = false,
            };
            DirectSoundCapture directSoundCapture = new DirectSoundCapture(DirectSoundGuid.DefaultVoiceCaptureDevice);
            CaptureBuffer captureBuffer = new CaptureBuffer(directSoundCapture, captureBufferDescription);

            const int bufferPortionCount = 8;
            int bufferPortionSize = captureBuffer.SizeInBytes / bufferPortionCount;

            List<NotificationPosition> notifications = new List<NotificationPosition>();
            for (int i = 0; i < bufferPortionCount; i++)
            {
                notifications.Add(new NotificationPosition()
                {
                    Offset = bufferPortionSize - 1 + bufferPortionSize * i,
                    Event = new AutoResetEvent(false),
                });
            }
            captureBuffer.SetNotificationPositions(notifications.ToArray());
            WaitHandle[] waitHandles = new WaitHandle[notifications.Count];
            for (int i = 0; i < notifications.Count; i++)
            {
                waitHandles[i] = notifications[i].Event;
            }

            captureBuffer.Start(true);
            #endregion

            #region Output
            
            DirectSound ds2 = new DirectSound(DirectSoundGuid.DefaultPlaybackDevice);
            ds2.SetCooperativeLevel(System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle, CooperativeLevel.Priority);

            SoundBufferDescription outDesc = new SoundBufferDescription()
            {
                Format = format,
                Flags = BufferFlags.GlobalFocus,
                SizeInBytes = 4 * format.AverageBytesPerSecond
            };

            PrimarySoundBuffer pBuffer = new PrimarySoundBuffer(ds2, outDesc);

            SoundBufferDescription outDesc2 = new SoundBufferDescription()
            {
                Format = format,
                Flags = BufferFlags.GlobalFocus | BufferFlags.ControlPositionNotify | BufferFlags.GetCurrentPosition2,
                SizeInBytes = format.AverageBytesPerSecond / 2
            };

            SecondarySoundBuffer sBuffer1 = new SecondarySoundBuffer(ds2, outDesc2);

            NotificationPosition[] notifications2 = new NotificationPosition[2];
            notifications2[0].Offset = outDesc2.SizeInBytes / 2 + 1;
            notifications2[1].Offset = outDesc2.SizeInBytes - 1; ;

            notifications2[0].Event = new AutoResetEvent(false);
            notifications2[1].Event = new AutoResetEvent(false);
            sBuffer1.SetNotificationPositions(notifications2);

            byte[] bytes1 = new byte[outDesc2.SizeInBytes / 2];
            byte[] bytes2 = new byte[outDesc2.SizeInBytes];

            Thread fillBuffer = new Thread(() =>
            {
                try
                {
                    var reader = stream.GetReader();
                    int readNumber = 1;
                    int bytesRead;

                    bytesRead = reader.Read(bytes2, 0, outDesc2.SizeInBytes);
                    sBuffer1.Write<byte>(bytes2, 0, LockFlags.None);
                    sBuffer1.Play(0, PlayFlags.Looping);
                    while (true)
                    {
                        //if (bytesRead == 0) { break; }
                        notifications2[0].Event.WaitOne();
                        bytesRead = reader.Read(bytes1, 0, bytes1.Length);
                        sBuffer1.Write<byte>(bytes1, 0, LockFlags.None);

                        //if (bytesRead == 0) { break; }
                        notifications2[1].Event.WaitOne();
                        bytesRead = reader.Read(bytes1, 0, bytes1.Length);
                        sBuffer1.Write<byte>(bytes1, outDesc2.SizeInBytes / 2, LockFlags.None);
                    }
                }
                catch
                {
                    Console.WriteLine("Thread quit.");
                }
                finally
                {
                    stream.Dispose();
                }
            });
            fillBuffer.Start();
            #endregion

            #region Init
            var bufferPortion = new byte[bufferPortionSize];
            var i16bufPortion = new Int16[bufferPortionSize / sizeof(Int16)];
            var i16bufPortion2 = new Int16[bufferPortionSize / sizeof(Int16)];
            var dblbufPortion = new double[2 * bufferPortionSize / sizeof(Int16)];
            uint sampleRate = (uint)format.SamplesPerSecond;// (uint)Math.Pow(2, Math.Ceiling(Math.Log(i16bufPortion.Length, 2)));
            vector = new double[2 * sampleRate];
            var vec2 = new double[2 * sampleRate];
            int bufferPortionIndex;
            DateTime lastNotificationTime = DateTime.MinValue;
            DateTime now;
            var fake = new float[i16bufPortion.Length];
            for (int i = 0; i < fake.Length; i++)
            {
                if (i % 40 < 11)
                    fake[i] = -1f / 4;
                else if (i % 40 < 21)
                    fake[i] = 0f;
                else if (i % 40 < 31)
                    fake[i] = 1f / 4;
                else
                    fake[i] = 0f;
            }
            #endregion

            while (GetAsyncKeyState(VK_ESCAPE) == 0)
            {
                bufferPortionIndex = WaitHandle.WaitAny(waitHandles);

                captureBuffer.Read<byte>(bufferPortion, 0, bufferPortionSize, bufferPortionSize * bufferPortionIndex);

                now = DateTime.Now;
                System.Diagnostics.Debug.Write(String.Format("{0}\t{1}\n", bufferPortionIndex, (now - lastNotificationTime).Milliseconds));
                lastNotificationTime = now;

                Buffer.BlockCopy(bufferPortion, 0, i16bufPortion, 0, bufferPortion.Length);
                for (int i = 0; i < i16bufPortion.Length; i++)
                {
                    dblbufPortion[2 * i] = i16bufPortion[i] / -(double)Int16.MinValue;
                    dblbufPortion[2 * i + 1] = 0;
                }
                ComplexFFT(dblbufPortion, (ulong)i16bufPortion.Length, sampleRate / 2, 1);
                /*for (int f = 0; f < 200; f++)
                {
                    vec2[2 * f] = vector[2 * f];
                    vec2[2 * f + 1] = vector[2 * f + 1];
                }
                for (int f = 200; f < sampleRate / 2; f++)
                {
                    vec2[2 * f] = 0;
                    vec2[2 * f + 1] = 0;
                }
                for (int f = 100; f < sampleRate / 4; f++)
                {
                    /*Complex c1 = new Complex(vector[4 * f - 2], vector[4 * f - 1]);
                    Complex c2 = new Complex(vector[4 * f + 2], vector[4 * f + 3]);
                    Complex c3 = new Complex(vector[4 * f], vector[4 * f + 1]);
                    Complex c4 = Complex.Sqrt(c1 * c2);
                    Complex c5 = Complex.Sqrt(c3 * c4);

                    vec2[2 * f] += c5.Real;
                    vec2[2 * f + 1] += c5.Imaginary;* /
                    vec2[2 * f] += vector[4 * f] + (vector[4 * f - 2] + vector[4 * f + 2]) / 2;
                    vec2[2 * f + 1] += vector[4 * f + 1] + (vector[4 * f - 1] + vector[4 * f + 3]) / 2;
                }
                for (int f = (int)sampleRate / 2; f < sampleRate / 2; f++)
                {
                    vec2[2 * f] = vector[2 * f];
                    vec2[2 * f + 1] = vector[2 * f + 1];
                }/**/
                for (int f = 0; f < sampleRate / 2; f++)
                {
                    vec2[f] = vector[f];
                    vec2[f + sampleRate / 2] = 0;
                    //vec2[vec2.Length - 2 - 2 * f] = 0;// vec2[2 * f];// vector[f] / 2;
                    //vec2[vec2.Length - 1 - 2 * f] = 0;// vec2[2 * f + 1];
                }/**/
                ComplexFFT(vec2, (ulong)sampleRate / 2, sampleRate, -1);
                for (int i = 0; i < i16bufPortion.Length; i++)
                {
                    double mag = vector[2 * i] / (sampleRate);
                    if (mag > 1)
                        mag = 1;
                    else if (mag < -1)
                        mag = -1;
                    //i16bufPortion[i] /= 8;
                    i16bufPortion[i] = (Int16)(mag * -(double)Int16.MinValue + 0.5);
                    //i16bufPortion[i] += (Int16)(i16bufPortion2[i] / 2);
                }
                Buffer.BlockCopy(i16bufPortion, 0, bufferPortion, 0, bufferPortion.Length);
                Buffer.BlockCopy(i16bufPortion, 0, i16bufPortion2, 0, bufferPortion.Length);
                //double sum = i16bufPortion.Sum((i16) => ((double)i16));
                //if (sum == 0)
                    //Buffer.BlockCopy(fake, 0, bufferPortion, 0, bufferPortion.Length);

                stream.Write(bufferPortion, 0, bufferPortion.Length);
            }

            captureBuffer.Stop();
            fillBuffer.Abort();

            for (int i = 0; i < notifications.Count; i++)
            {
                notifications[i].Event.Close();
            }

            captureBuffer.Dispose();
            directSoundCapture.Dispose();
            ds.Dispose();

        }

        static void SWAP(ref double d1, ref double d2)
        {
            double temp = d1;
            d1 = d2;
            d2 = temp;
        }

        static double[] vector;
        
        static void ComplexFFT(double[] data, ulong number_of_samples, uint sample_rate, int sign)
        {

	        //variables for the fft
	        ulong n,mmax,m,j,istep,i;
	        double wtemp,wr,wpr,wpi,wi,theta,tempr,tempi;

	        //the complex array is real+complex so the array 
            //as a size n = 2* number of complex samples
            //real part is the data[index] and 
            //the complex part is the data[index+1]

	        //new complex array of size n=2*sample_rate
            //double[] vector = data;// new double[2 * sample_rate];

	        //put the real array in a complex array
	        //the complex part is filled with 0's
	        //the remaining vector with no data is filled with 0's
	        for(n=0; n<sample_rate;n++)
	        {
                if (n < number_of_samples)
                {
                    vector[2 * n] = data[2 * n];
                    vector[2 * n + 1] = data[2 * n + 1];
                }
                else
                {
                    vector[2 * n] = 0;
                    vector[2 * n + 1] = 0;
                }
	        }

	        //binary inversion (note that the indexes 
            //start from 0 witch means that the
            //real part of the complex is on the even-indexes 
            //and the complex part is on the odd-indexes)
	        n=sample_rate << 1;
	        j=0;
	        for (i=0;i<n/2;i+=2) {
		        if (j > i) {
			        SWAP(ref vector[j],ref vector[i]);
                    SWAP(ref vector[j + 1], ref vector[i + 1]);
			        if((j/2)<(n/4)){
                        SWAP(ref vector[(n - (i + 2))], ref vector[(n - (j + 2))]);
                        SWAP(ref vector[(n - (i + 2)) + 1], ref vector[(n - (j + 2)) + 1]);
			        }
		        }
		        m=n >> 1;
		        while (m >= 2 && j >= m) {
			        j -= m;
			        m >>= 1;
		        }
		        j += m;
	        }
	        //end of the bit-reversed order algorithm

	        //Danielson-Lanzcos routine
	        mmax=2;
	        while (n > mmax) {
		        istep=mmax << 1;
		        theta=sign*(2*Math.PI/mmax);
		        wtemp=Math.Sin(0.5*theta);
		        wpr = -2.0*wtemp*wtemp;
		        wpi=Math.Sin(theta);
		        wr=1.0;
		        wi=0.0;
		        for (m=1;m<mmax;m+=2) {
			        for (i=m;i<=n;i+=istep) {
				        j=i+mmax;
				        tempr=wr*vector[j-1]-wi*vector[j];
				        tempi=wr*vector[j]+wi*vector[j-1];
				        vector[j-1]=vector[i-1]-tempr;
				        vector[j]=vector[i]-tempi;
				        vector[i-1] += tempr;
				        vector[i] += tempi;
			        }
			        wr=(wtemp=wr)*wpr-wi*wpi+wr;
			        wi=wi*wpr+wtemp*wpi+wi;
		        }
		        mmax=istep;
	        }
	        //end of the algorithm
	
	        /*
            //determine the fundamental frequency
	        //look for the maximum absolute value in the complex array
	        fundamental_frequency=0;
	        for(i=2; i<=sample_rate; i+=2)
	        {
		        if((pow(vector[i],2)+pow(vector[i+1],2))>(pow(vector[fundamental_frequency],2)+pow(vector[fundamental_frequency+1],2))){
			        fundamental_frequency=i;
		        }
	        }

	        //since the array of complex has the format [real][complex]=>[absolute value]
	        //the maximum absolute value must be ajusted to half
	        fundamental_frequency=(long)floor((float)fundamental_frequency/2);
            */
        }
    }
}
