using LibVLCSharp.Shared;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace DeferredEngine.Logic
{
    public class VideoIntroLogic
    {
        private LibVLC? _libVLC;
        private MediaPlayer? _mediaPlayer;
        private Texture2D? _videoTexture;
        private bool _isPlaying;
        private bool _hasFinished;
        
        private byte[]? _frameBuffer;
        private GCHandle _bufferHandle;

        public bool HasFinished => _hasFinished;

        // Note: ContentManager is in Microsoft.Xna.Framework.Content
        public void Load(ContentManager content, GraphicsDevice graphicsDevice)
        {
            try
            {
                Core.Initialize();

                // Add these arguments to stabilize the manual rendering mode
                string[] options = new string[] 
                { 
                    "--avcodec-hw=none",// Disable hardware decoding to fix converter errors
                    "--no-video-title-show", // Hide filename at start
                    "--aout=directx", // Use DirectX audio instead of Windows MMDevice
                    "--vout=drawable-nsobject", // Prevents LibVLC from trying to create its own window
                    "--quiet" // Suppress VLC logs
                };
                
                _libVLC = new LibVLC(options);
                _mediaPlayer = new MediaPlayer(_libVLC);

                string path = Path.Combine(AppContext.BaseDirectory, content.RootDirectory, "intro.mp4");
                if (!File.Exists(path)) { _hasFinished = true; return; }

                var media = new Media(_libVLC, path, FromType.FromPath);
                
                // Set video to 1080p format
                uint width = 1920;
                uint height = 1080;
                uint pitch = width * 4;

                _mediaPlayer.SetVideoFormat("RV32", width, height, pitch);
                _frameBuffer = new byte[pitch * height];
                _bufferHandle = GCHandle.Alloc(_frameBuffer, GCHandleType.Pinned);

                // This is the "Unsafe" part that requires the .csproj change
                unsafe
                {
                    _mediaPlayer.SetVideoCallbacks((opaque, planes) =>
                    {
                        // 1. Cast the 'planes' (nint) to a pointer of pointers (void**)
                        void** p = (void**)planes;
                        
                        // 2. Assign the address of your pinned buffer to the first plane
                        p[0] = (void*)_bufferHandle.AddrOfPinnedObject();
                        
                        return IntPtr.Zero;
                    }, null, null);
                }

                _videoTexture = new Texture2D(graphicsDevice, (int)width, (int)height, false, SurfaceFormat.Bgra32);
                _mediaPlayer.Media = media;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("VLC Load Error: " + ex.Message);
                _hasFinished = true;
            }
        }

        public void Initialize()
        {
            if (_mediaPlayer == null) return;
            _mediaPlayer.Play();
            _isPlaying = true;
        }

        public void Update()
        {
            if (_mediaPlayer == null || !_isPlaying) return;

            if (_mediaPlayer.State == VLCState.Ended || _mediaPlayer.State == VLCState.Error)
            {
                _hasFinished = true;
                _isPlaying = false;
            }
        }

        private void StopVideo()
        {
            if (_mediaPlayer != null)
            {
                _mediaPlayer.Stop();
            }
            _isPlaying = false;
            _hasFinished = true;
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (_hasFinished || _videoTexture == null || _frameBuffer == null) return;

            // Upload raw pixels to the texture
            _videoTexture.SetData(_frameBuffer);
            
            spriteBatch.Draw(_videoTexture, spriteBatch.GraphicsDevice.Viewport.Bounds, Color.White);
        }

        public void Unload()
        {
            _mediaPlayer?.Stop();
            _mediaPlayer?.Dispose();
            _libVLC?.Dispose();
            if (_bufferHandle.IsAllocated) _bufferHandle.Free();
            _videoTexture?.Dispose();
        }
    }
}