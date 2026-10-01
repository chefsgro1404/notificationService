namespace NotificationService.Domain.Enums;

/// <summary>
/// Lifecycle status of a text-to-speech (T2A) audio record.
/// A record starts as <see cref="AudioNotGenerated"/> and becomes <see cref="Active"/> once the WAV file is
/// stored. Only <see cref="Active"/> audio is playable; <see cref="Deleted"/> is a soft delete that keeps the
/// record (and its integer id) so it can be restored.
/// </summary>
public enum AudioStatus
{
    AudioNotGenerated = 0,
    Active = 1,
    Inactive = 2,
    Deleted = 3
}
