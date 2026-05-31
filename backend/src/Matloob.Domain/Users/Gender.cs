namespace Matloob.Domain.Users;

/// <summary>
/// User gender. Laravel stored this as a free string ('male' / 'female');
/// modeled here as an enum and persisted/serialized as the same lowercase
/// wire token via <see cref="UserProfileWire"/>.
/// </summary>
public enum Gender
{
    Male = 1,
    Female = 2,
}
