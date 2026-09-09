namespace SPTFreeAim.Core
{
    /// <summary>
    /// How the weapon offset is turned into an actual rotation.
    ///
    /// This exists because the original way was never a clean rotation about a
    /// point, and reading the game's own code is what showed it
    /// (docs/07-FINDINGS.md F22).
    /// </summary>
    public enum HingeMode
    {
        /// <summary>
        /// AROUND THE GRIP, in world space. The physical description: the right
        /// hand holds the grip, the mouse moves the support hand, and the weapon
        /// is the rigid link between them, so it turns about the grip.
        ///
        /// Yaw turns about the world vertical and pitch about the camera's own
        /// right vector - the two axes those words actually mean. Nothing is
        /// reinterpreted through the weapon's local frame on the way, so there
        /// is no axis to discover per weapon and no invert toggle needed to make
        /// pitch appear.
        /// </summary>
        AroundGrip = 0,

        /// <summary>
        /// The original call, kept for comparison: TransformTools.LocalRotateAround
        /// with (pitch, 0, yaw) as its euler argument.
        ///
        /// That method does NOT take euler angles in the weapon's local space. It
        /// takes the vector, calls parent.TransformDirection on it, calls
        /// InverseTransformDirection on the result, and only then treats it as
        /// euler angles. So which way the weapon turns depends on how it happens
        /// to be oriented relative to its parent, and a component of the mouse
        /// movement can land along the barrel, where it rolls the gun instead of
        /// aiming it. Inherited from lualeet and never read until F22.
        /// </summary>
        LegacyEuler = 1
    }
}
