using LibVLCSharp.Shared;
using Engine.Editor;
using Engine.Recources;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Engine.Logic
{
    /// <summary>
    /// Plays the intro videos of <see cref="IntroVideoList"/> back to back before the game starts.
    /// Enter skips the current video; the intro finishes after the last one.
    /// </summary>
    public class VideoIntroLogic
    {
        private LibVLC? _libVLC;
        private MediaPlayer? _mediaPlayer;
        private Texture2D? _videoTexture;
        private bool _isPlaying;
        private bool _hasFinished;

        private byte[]? _frameBuffer;
        private GCHandle _bufferHandle;

        // Absolute paths in play order, and the one playing now.
        private readonly List<string> _playlist = new List<string>();
        private int _current = -1;
        // Set from LibVLC's event thread when the current video ends or fails.
        private volatile bool _videoDone;
        private KeyboardState _previousKeys;

        public bool HasFinished => _hasFinished;

        // Note: ContentManager is in Microsoft.Xna.Framework.Content
        public void Load(ContentManager content, GraphicsDevice graphicsDevice)
        {
            foreach (string entry in IntroVideoList.Read().Videos)
            {
                string path = IntroVideoList.ResolvePath(entry);
                if (File.Exists(path)) _playlist.Add(path);
                else EditorBridge.Log($"Intro video not found: '{entry}'");
            }
            if (_playlist.Count == 0) { _hasFinished = true; return; }

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
                _mediaPlayer.EndReached += (_, _) => _videoDone = true;
                _mediaPlayer.EncounteredError += (_, _) => _videoDone = true;

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
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("VLC Load Error: " + ex.Message);
                _hasFinished = true;
            }
        }

        public void Initialize()
        {
            if (_mediaPlayer == null || _hasFinished) return;
            // An Enter already held at startup must not skip the first video.
            _previousKeys = Keyboard.GetState();
            _isPlaying = true;
            PlayNext();
        }

        public void Update(bool isActive)
        {
            if (_mediaPlayer == null || !_isPlaying) return;

            KeyboardState keys = Keyboard.GetState();
            bool skip = isActive && keys.IsKeyDown(Keys.Enter) && !_previousKeys.IsKeyDown(Keys.Enter);
            _previousKeys = keys;

            if (skip || _videoDone || _mediaPlayer.State == VLCState.Error)
                PlayNext();
        }

        // Starts the next video of the playlist, or finishes the intro after the last one.
        private void PlayNext()
        {
            _current++;
            if (_current >= _playlist.Count) { StopVideo(); return; }

            _mediaPlayer!.Stop();
            // Don't show the previous video's last frame while the next one opens.
            if (_frameBuffer != null) Array.Clear(_frameBuffer);
            _videoDone = false;

            using var media = new Media(_libVLC!, _playlist[_current], FromType.FromPath);
            _mediaPlayer.Play(media);
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
