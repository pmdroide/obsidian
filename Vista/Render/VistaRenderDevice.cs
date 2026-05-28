using AngleSharp.Css;     // Required for IRenderDevice and DeviceCategory
using AngleSharp.Css.Dom; // Required for IRenderDimensions

namespace Vista
{
    public class VistaRenderDevice : IRenderDevice
    {
        // 1. Core metric needed to prevent relative unit calculation crashes
        public double FontSize { get; set; } = 16.0;

        // 2. Viewport bounds (integers)
        public int ViewPortWidth { get; set; } = 1920;
        public int ViewPortHeight { get; set; } = 1080;

        // 3. Physical display properties (integers)
        public int DeviceWidth { get; set; } = 1920;
        public int DeviceHeight { get; set; } = 1080;

        // 4. IRenderDimensions interface requirements (strictly typed as double per your compiler errors)
        public double RenderWidth { get; set; } = 1920.0;
        public double RenderHeight { get; set; } = 1080.0;

        // 5. Hardware properties & refresh settings
        public int Resolution { get; set; } = 96;
        public int Frequency { get; set; } = 60;
        public bool IsGrid { get; set; } = false;

        // 6. Missing features reported by the compiler errors:
        public bool IsInterlaced => false;
        public bool IsScripting => true;
        public int ColorBits => 32;
        public int MonochromeBits => 0;

        // 7. Device Category classification enum
        public DeviceCategory Category => DeviceCategory.Screen;
    }
}