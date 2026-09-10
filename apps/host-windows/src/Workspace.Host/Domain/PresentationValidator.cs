namespace Workspace.Host.Domain;

public static class PresentationValidator
{
    public static bool IsValid(PresentationState presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        static bool FiniteVector(Vec3 value) =>
            double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);
        static bool FiniteQuaternion(Quaternion value) =>
            double.IsFinite(value.X)
            && double.IsFinite(value.Y)
            && double.IsFinite(value.Z)
            && double.IsFinite(value.W);

        var rotationLengthSquared = presentation.Rotation.X * presentation.Rotation.X
            + presentation.Rotation.Y * presentation.Rotation.Y
            + presentation.Rotation.Z * presentation.Rotation.Z
            + presentation.Rotation.W * presentation.Rotation.W;
        return FiniteVector(presentation.Position)
            && FiniteVector(presentation.Size)
            && presentation.Size.X > 0
            && presentation.Size.Y > 0
            && presentation.Size.Z > 0
            && FiniteQuaternion(presentation.Rotation)
            && rotationLengthSquared > 1e-12;
    }
}
