﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duality;
using Duality.Components;
using Duality.Drawing;
using Duality.Editor;
using Duality.Resources;
using OpenTK;
using OpenTK.Graphics.OpenGL;

#if __ANDROID__
using Android.Content.Res;
#endif

namespace OgvPlayer
{
	[Serializable]
	public class OgvComponent : Renderer, ICmpInitializable, ICmpUpdatable
	{
		private string _fileName;
		[NonSerialized]
		private double _startTime;
		[NonSerialized]
		private Texture _textureOne;
		[NonSerialized]
		private Texture _textureTwo;
		[NonSerialized]
		private Texture _textureThree;
		[NonSerialized]
		private float _elapsedFrameTime;

		[NonSerialized]
		private TheoraVideo _theoraVideo;

		[NonSerialized]
		private FmodTheoraStream _fmodTheoraStream;
		[NonSerialized]
		private VertexC1P3T2[] _vertices;
		[NonSerialized]
		private MediaState _state;
		[NonSerialized]
		private Canvas _canvas;

		private Task _audioThread;

		public string FileName
		{
			get { return _fileName; }
			set
			{
				if (!value.StartsWith("video", StringComparison.OrdinalIgnoreCase))
				{
					value = "video\\" + value.TrimStart('\\');
				}
				_fileName = value;
			}
		}

		[EditorHintFlags(MemberFlags.Invisible)]
		public MediaState State
		{
			get { return _state; }
			private set { _state = value; }
		}

		[EditorHintFlags(MemberFlags.Invisible)]
		public bool IsFinished 
		{ 
			get
		    {
				return _theoraVideo == null || _theoraVideo.IsFinished;
		    }
		}

	    public bool CanRunOnThisArchitecture { get { return !Environment.Is64BitProcess; } }

		public ContentRef<Material> Material { get; set; }

		[EditorHintFlags(MemberFlags.Invisible)]
		public override float BoundRadius
		{
			get
			{
				return Rect.Transform(GameObj.Transform.Scale, GameObj.Transform.Scale).BoundingRadius;
			}
		}

		public Rect Rect { get; set; }
		public ColorRgba ColourTint { get; set; }
		public ScreenAspectOptions ScreenAspectOptions { get; set; }

		public void OnInit(InitContext context)
		{
			if (context != InitContext.Activate || DualityApp.ExecContext == DualityApp.ExecutionContext.Editor)
				return;

			if (string.IsNullOrEmpty(_fileName))
				return;

		    if (Environment.Is64BitProcess)
		    {
		        Log.Editor.WriteWarning("The video player is not supported on 64 bit processes, and this is one.");
                return;
		    }

			Play();
		}

		public void OnShutdown(ShutdownContext context)
		{
			if (context != ShutdownContext.Deactivate)
				return;

			Stop();
		}

		internal void Initialize()
		{
			Stop();

		    _fmodTheoraStream = new FmodTheoraStream();
			_fmodTheoraStream.Initialize();

			_theoraVideo = new TheoraVideo();

#if __ANDROID__
			_fileName = ExtractVideoFromAPK(_fileName);
#endif
			_theoraVideo.InitializeVideo(_fileName);

			_textureOne = new Texture(_theoraVideo.Width, _theoraVideo.Height, filterMin: TextureMinFilter.Linear);
			_textureTwo = new Texture(_theoraVideo.Width / 2, _theoraVideo.Height / 2, filterMin: TextureMinFilter.Linear);
			_textureThree = new Texture(_theoraVideo.Width / 2, _theoraVideo.Height / 2, filterMin: TextureMinFilter.Linear);
		}

		private void StopVideoIfRunning()
		{
			if (_theoraVideo != null && CanRunOnThisArchitecture)
			{
				_theoraVideo.Terminate();
				_theoraVideo = null;
			}
		}

		private void DecodeAudio()
		{
			const int bufferSize = 4096 * 2;

			while (State != MediaState.Stopped && _theoraVideo != null)
			{
				var theoraDecoder = _theoraVideo.TheoraDecoder;

				while (State != MediaState.Stopped && TheoraPlay.THEORAPLAY_availableAudio(theoraDecoder) == 0)
				{
					// don't use all of the cpu while waiting for data
					Thread.Sleep(1);

					// if the game object has somehow been disposed with the state being set to stopped, then the thread will never
					// exit, so check for that explicitly here
					if (GameObj != null && GameObj.Disposed)
						return;
				}

				var data = new List<float>();
				TheoraPlay.THEORAPLAY_AudioPacket currentAudio;
				while (data.Count < bufferSize && TheoraPlay.THEORAPLAY_availableAudio(theoraDecoder) > 0)
				{
					var audioPtr = TheoraPlay.THEORAPLAY_getAudio(theoraDecoder);
					currentAudio = TheoraPlay.getAudioPacket(audioPtr);
					data.AddRange(TheoraPlay.getSamples(currentAudio.samples, currentAudio.frames * currentAudio.channels));
					TheoraPlay.THEORAPLAY_freeAudio(audioPtr);
				}

				if (State == MediaState.Playing)
					_fmodTheoraStream.Stream(data.ToArray());
			}
		}

		public void OnUpdate()
		{
			if (State != MediaState.Playing)
				return;
			if (Time.GameTimer.TotalMilliseconds - _startTime < 800)
				return;
			if(!CanRunOnThisArchitecture)
                return;
			_elapsedFrameTime += Time.LastDelta * Time.TimeScale;
			
		    if (_theoraVideo != null && CanRunOnThisArchitecture )
			{
				_theoraVideo.UpdateVideo(_elapsedFrameTime);
		        if(_theoraVideo.IsFinished)
		            Stop();
		    }
		}

		public void Play()
		{
			try
			{
				if (State != MediaState.Stopped)
					return;

				if(!CanRunOnThisArchitecture)
					Log.Game.WriteWarning("Can't play video on this architecture sorry ");
				WaitForAndDisposeAudioThread();

				if (_theoraVideo == null || _theoraVideo.Disposed)
					Initialize();

				State = MediaState.Playing;
				_audioThread = Task.Factory.StartNew(DecodeAudio);

				_startTime = (float)Time.GameTimer.TotalMilliseconds;
				_elapsedFrameTime = 0;
			}
			catch(Exception exception)
			{
				Log.Game.WriteWarning("Video component failed with error {0}",Log.Exception(exception) );
			}
		}

		public void Stop()
		{
			if (State == MediaState.Stopped)
				return;

			State = MediaState.Stopped;
			WaitForAndDisposeAudioThread();

			if (!CanRunOnThisArchitecture) return;

			if (_fmodTheoraStream != null)
				_fmodTheoraStream.Stop();

			StopVideoIfRunning();
		}

		public override bool IsVisible(IDrawDevice device)
		{
			if ((device.VisibilityMask & VisibilityFlag.ScreenOverlay) != (VisibilityGroup & VisibilityFlag.ScreenOverlay))
				return false;

			if ((VisibilityGroup & device.VisibilityMask & VisibilityFlag.AllGroups) == VisibilityFlag.None)
				return false;

			return device.IsCoordInView(GameObj.Transform.Pos, BoundRadius);
		}

		public override void Draw(IDrawDevice device)
		{
			DrawDesignTimeVisuals(device);

			if (State != MediaState.Playing)
				return;

			if (_theoraVideo == null || _theoraVideo.ElapsedMilliseconds == 0)
				return;

			Texture.Bind(_textureOne);
			GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, _theoraVideo.Width, _theoraVideo.Height, PixelFormat.Luminance, PixelType.UnsignedByte,
				_theoraVideo.GetYColorPlane());

			Texture.Bind(_textureTwo);
			GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, _theoraVideo.Width / 2, _theoraVideo.Height / 2, PixelFormat.Luminance, PixelType.UnsignedByte,
				_theoraVideo.GetCbColorPlane());

			Texture.Bind(_textureThree);
			GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, _theoraVideo.Width / 2, _theoraVideo.Height / 2, PixelFormat.Luminance, PixelType.UnsignedByte,
				_theoraVideo.GetCrColorPlane());

			var drawTechnique = (OgvDrawTechnique)Material.Res.Technique.Res;
			drawTechnique.TextureOne = _textureOne;
			drawTechnique.TextureTwo = _textureTwo;
			drawTechnique.TextureThree = _textureThree;

			var rect = GetScreenRect();
			var z = GameObj.Transform == null ? 0 : GameObj.Transform.Pos.Z;

			if (_canvas == null)
			{
				_canvas = new Canvas(device);
				_canvas.State.SetMaterial(Material);
			}
			
			_canvas.State.ColorTint = ColourTint;
			_canvas.FillRect(rect.X, rect.Y, z, rect.W, rect.H);
		}

		private Rect GetScreenRect()
		{
			var rect = Rect.Empty;
			if (ScreenAspectOptions == ScreenAspectOptions.MaintainAspectRatio)
			{
				var videoRatio = (float) _theoraVideo.Width/_theoraVideo.Height;
				var screenRatio = DualityApp.TargetResolution.X/DualityApp.TargetResolution.Y;

				if (videoRatio > screenRatio)
				{
					rect.W = DualityApp.TargetResolution.X;
					rect.H = rect.W / videoRatio;
					rect.Y = (DualityApp.TargetResolution.Y - rect.H) / 2;
				}
				else
				{
					rect.H = DualityApp.TargetResolution.Y;
					rect.W = rect.H * videoRatio;
					rect.X = (DualityApp.TargetResolution.X - rect.W) / 2;
				}
			}
			else if(ScreenAspectOptions == ScreenAspectOptions.FillScreen)
			{
				rect = new Rect(0, 0, DualityApp.TargetResolution.X, DualityApp.TargetResolution.Y);
			}
			else
			{
				rect = new Rect(GameObj.Transform.Pos.X, GameObj.Transform.Pos.Y, Rect.W, Rect.H);
				rect = new Rect(rect.X, rect.Y, rect.W * GameObj.Transform.Scale, rect.H * GameObj.Transform.Scale);
			}
			
			return rect;
		}

		private void DrawDesignTimeVisuals(IDrawDevice device)
		{
			if (DualityApp.ExecContext != DualityApp.ExecutionContext.Editor)
				return;

			if (device == null)
				return;

			var canvas = new Canvas(device);
			canvas.State.TransformScale = new Vector2(GameObj.Transform.Scale);
			canvas.DrawRect(GameObj.Transform.Pos.X + Rect.MinimumX, GameObj.Transform.Pos.Y + Rect.MinimumY, GameObj.Transform.Pos.Z, Rect.W, Rect.H);
		}

		private void WaitForAndDisposeAudioThread()
		{
			if (_audioThread != null && _audioThread.Status == TaskStatus.Running)
			{
				_audioThread.Wait();
				_audioThread = null;
			}
		}

#if __ANDROID__
		private string ExtractVideoFromAPK(string fileName)
		{
			fileName = fileName.Replace("\\", "/");
			var destinationPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), fileName);

			if (File.Exists(destinationPath))
				return destinationPath;

			if (Directory.Exists(Path.GetDirectoryName(destinationPath)) == false)
				Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));

			try
			{
				using (var stream = ContentProvider.OpenAsset(fileName))
				using (var fileStream = new FileStream(destinationPath, FileMode.Create))
				{
					Log.Game.Write("Copying video file {0} to {1}", fileName, destinationPath);
					stream.CopyTo(fileStream);
					fileStream.Flush();
				}
			}
			catch (Exception e)
			{
				Log.Game.Write("Couldn't extract file {0} from the APK: {1}", fileName, e.Message);
			}
			return destinationPath;
		}
#endif
	}
}
