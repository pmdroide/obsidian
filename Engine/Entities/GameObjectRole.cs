namespace Engine.Entities
{
    /// <summary>
    /// What a gameobject is for, independent of its components (Inspector > Role).
    /// Scenes save the name, so never rename a value; add new roles at the end.
    /// </summary>
    public enum GameObjectRole
    {
        /// <summary>An ordinary object.</summary>
        Default,
        /// <summary>
        /// A water volume: everything below its top surface (inside its XY footprint) is water.
        /// Physics bodies with Buoyancy float in it, and it never builds a collider of its own.
        /// </summary>
        Water,
    }
}
