using System;
using System.Reflection;
using Microsoft.Xna.Framework.Graphics;

namespace Engine.Renderer.Helper
{
    internal static class GraphicsDeviceDebugMarkers
    {
        private static readonly MethodInfo s_beginEventMethod;
        private static readonly MethodInfo s_endEventMethod;

        static GraphicsDeviceDebugMarkers()
        {
            Type graphicsDeviceType = typeof(GraphicsDevice);
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            s_beginEventMethod = graphicsDeviceType.GetMethod("BeginEventGroup", flags, null, new[] { typeof(string) }, null)
                ?? graphicsDeviceType.GetMethod("BeginEvent", flags, null, new[] { typeof(string) }, null)
                ?? graphicsDeviceType.GetMethod("PushEvent", flags, null, new[] { typeof(string) }, null)
                ?? graphicsDeviceType.GetMethod("PushMarker", flags, null, new[] { typeof(string) }, null);

            s_endEventMethod = graphicsDeviceType.GetMethod("EndEventGroup", flags, null, Type.EmptyTypes, null)
                ?? graphicsDeviceType.GetMethod("EndEvent", flags, null, Type.EmptyTypes, null)
                ?? graphicsDeviceType.GetMethod("PopEvent", flags, null, Type.EmptyTypes, null)
                ?? graphicsDeviceType.GetMethod("PopMarker", flags, null, Type.EmptyTypes, null);
        }

        public static void BeginEventGroup(GraphicsDevice graphicsDevice, string name)
        {
            if (graphicsDevice == null || s_beginEventMethod == null)
                return;

            try
            {
                s_beginEventMethod.Invoke(graphicsDevice, new object[] { name });
            }
            catch
            {
                // Swallow any reflection or runtime invocation errors so markers remain optional.
            }
        }

        public static void EndEventGroup(GraphicsDevice graphicsDevice)
        {
            if (graphicsDevice == null || s_endEventMethod == null)
                return;

            try
            {
                s_endEventMethod.Invoke(graphicsDevice, null);
            }
            catch
            {
                // Swallow any reflection or runtime invocation errors so markers remain optional.
            }
        }
    }
}
