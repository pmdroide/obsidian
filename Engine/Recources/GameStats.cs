using Engine.Logic;

namespace Engine.Recources
{
    public static class GameStats
    {
        //Meshes skipped by occlusion culling this frame (GameSettings.g_occlusionculling)
        public static int OccludedMeshes;
        //Lightmap Static gameobjects drawn with their lightmap this frame (Source 2 look, phase 4.2)
        public static int LightmappedMeshes;
        public static int MeshDraws = 0;
        public static int MaterialDraws = 0;
        public static int LightsDrawn = 0;

        public static int shadowMaps = 0;
        public static int activeShadowMaps = 0;
        public static int EmissiveMeshDraws = 0;

        public static long d_profileRenderChanges;
        public static long d_profileDrawShadows;
        public static long d_profileDrawCubeMap;
        public static long d_profileUpdateViewProjection;
        public static long d_profileSetupGBuffer;
        public static long d_profileDrawGBuffer;
        public static long d_profileDrawHolograms;
        public static long d_profileDrawScreenSpaceEffect;
        public static long d_profileDrawScreenSpaceDirectionalShadow;
        public static long d_profileDrawBilateralBlur;
        public static long d_profileDrawLights;
        public static long d_profileDrawEnvironmentMap;
        public static long d_profileDrawEmissive;
        public static long d_profileDrawSSR;
        public static long d_profileCompose;
        public static long d_profileCombineTemporalAntialiasing;
        public static long d_profileDrawFinalRender;
        public static long d_profileTotalRender;
        
        // Froxel stats
        public static long d_froxelBuildTime = 0;
        public static bool d_froxelSystemReady = false;
        public static int d_froxelsLightCount = 0;
        
        public static bool UIIsHovered;
        public static bool e_EnableSelection = false;
        public static EditorLogic.GizmoModes e_gizmoMode = EditorLogic.GizmoModes.Translation;
        public static bool e_LocalTransformation = false;
        public static float sdf_load = 0;
    }
}
