namespace Engine.Renderer.Lighting
{
    public enum LightingBakeState
    {
        Idle,
        Baking,
        Completed,
        Cancelled,
        Failed,
    }

    /// <summary>
    /// Immutable progress report for a lighting bake. Published from the game thread and from the
    /// bake worker, so it is replaced wholesale (reference swap) rather than mutated.
    /// </summary>
    public sealed class LightingBakeStatus
    {
        public static readonly LightingBakeStatus Idle = new LightingBakeStatus(LightingBakeState.Idle, 0, "", null);

        public LightingBakeState State { get; }

        //0..1
        public float Progress { get; }

        //Human-readable current step ("Bounce 1/2 — ...")
        public string Stage { get; }

        //Final outcome / error text for Completed, Cancelled and Failed
        public string Message { get; }

        public bool IsBaking => State == LightingBakeState.Baking;

        public LightingBakeStatus(LightingBakeState state, float progress, string stage, string message)
        {
            State = state;
            Progress = progress;
            Stage = stage ?? "";
            Message = message;
        }
    }
}
