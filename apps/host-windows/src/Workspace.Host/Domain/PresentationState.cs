namespace Workspace.Host.Domain;

public readonly record struct Vec3(double X, double Y, double Z);

public readonly record struct Quaternion(double X, double Y, double Z, double W);

public sealed record PresentationState(
    Vec3 Position,
    Quaternion Rotation,
    Vec3 Size,
    string? ParentPresentationId = null,
    string? Representation = null)
{
    public static PresentationState Default { get; } = new(
        new Vec3(0, 0, 0),
        new Quaternion(0, 0, 0, 1),
        new Vec3(1, 1, 1));
}
