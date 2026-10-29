namespace VarjoEye.Core.Services.Baballonia.Sampling;

internal enum SampleRejectionReason
{
    None,
    MissingFeatures,
    InvalidFeatureDimensions,
    NonFiniteValues,
    InvalidTimestamp,
    MissingReferenceGaze,
    InvalidTrackingStatus,
    LowStability,
    TimestampMismatch,
    RateLimited,
    CapacityReached,
    InvalidBlinkTarget
}
