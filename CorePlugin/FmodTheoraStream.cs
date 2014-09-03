using System;
using System.Runtime.InteropServices;
using OgvPlayer.Fmod;

namespace OgvPlayer
{
	public class FmodTheoraStream
	{
		const int BufferSize = 1024768;

		private static Fmod.System _system;
		private static Sound _sound;
		private static Channel _channel;
		private static CREATESOUNDEXINFO _createsoundexinfo;
		private static bool _soundcreated;
		private static MODE mode = (Fmod.MODE._2D | Fmod.MODE.DEFAULT | Fmod.MODE.OPENUSER | Fmod.MODE.LOOP_NORMAL | Fmod.MODE.HARDWARE);
		
		private static CircularBuffer<float> _circularBuffer;
		private static object _syncObject = new object();

		public static void Init()
		{
			uint version = 0;
			RESULT result;
			const uint channels = 2;
			const uint frequency = 48000;
			_circularBuffer = new CircularBuffer<float>(BufferSize, true);
			
			result = Factory.System_Create(ref _system);
			//			ERRCHECK(result);
			result = _system.getVersion(ref version);
			//			ERRCHECK(result);
			//			if (version < Fmod.VERSION.number)
			//			{
			//				MessageBox.Show("Error!  You are using an old version of Fmod " + version.ToString("X") + ".  This program requires " + Fmod.VERSION.number.ToString("X") + ".");
			//				Application.Exit();
			//			}
			result = _system.init(32, INITFLAGS.NORMAL, (IntPtr)null);
			//			ERRCHECK(result);

			_createsoundexinfo.cbsize = Marshal.SizeOf(_createsoundexinfo);
			_createsoundexinfo.fileoffset = 0;
			_createsoundexinfo.length = frequency * channels * 2 * 2;
			_createsoundexinfo.numchannels = (int)channels;
			_createsoundexinfo.defaultfrequency = (int)frequency;
			_createsoundexinfo.format = SOUND_FORMAT.PCMFLOAT;
			_createsoundexinfo.pcmreadcallback += PcmReadCallback;
			_createsoundexinfo.dlsname = null;

			if (!_soundcreated)
			{
				result = _system.createSound( (string)null, (mode | MODE.CREATESTREAM),ref _createsoundexinfo,ref _sound);
				_soundcreated = true;
			}
			_system.playSound(CHANNELINDEX.FREE, _sound, false, ref _channel);
		}

		public static void Stream(float[] data)
		{
			lock (_syncObject)
			{
				
				_circularBuffer.Put(data);
			}
		}

		private static RESULT PcmReadCallback(IntPtr sounDraw, IntPtr data, uint datalen)
		{
			unsafe
			{
				uint count; //Does this need to be outside the lock? AM

				lock (_syncObject)
				{
					
					if (_circularBuffer.Size == 0)
						return RESULT.OK;

					var stereo32BitBuffer = (float*)data.ToPointer();
					for (count = 0; count < (datalen >> 2); count++) //WTF does this do AM
					{
						if (_circularBuffer.Size == 0)
							break;

						*stereo32BitBuffer++ = _circularBuffer.Get();
					}
					
				}
			}
			return RESULT.OK;
		}
	}
}